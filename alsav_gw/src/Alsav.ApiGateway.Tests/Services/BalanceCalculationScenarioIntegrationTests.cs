using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Controllers.Internal;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Tests.Services;

public sealed class BalanceCalculationScenarioIntegrationTests : IDisposable
{
    private const string Secret = "test-internal-secret";
    private const string Wh = "Склад адресного хранения";
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public BalanceCalculationScenarioIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"alsav-gw-scenario-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<GatewayDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddLogging();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task CalculateAsync_SnapshotOnly_AllMaterialsInSnapshot_ZeroMovements()
    {
        await SyncCompositionsAsync();

        var snapshotController = CreateRawController();
        await snapshotController.SyncStockSnapshot(new[]
        {
            Snap("mat1", 2), Snap("mat2", 0), Snap("mat3", 4),
            Snap("comp1", 0), Snap("comp2", 0),
            Snap("prod1", 0), Snap("prod2", 0), Snap("prod3", 0),
        }, CancellationToken.None);

        await SyncMovementsAsync(Array.Empty<StockMovementItem>());

        var byArticle = await CalculateAsync();
        AssertScenario(byArticle);
    }

    [Fact]
    public async Task CalculateAsync_SnapshotPlusMovements_OnlyMaterialsInSnapshot()
    {
        await SyncCompositionsAsync();

        var snapshotController = CreateRawController();
        await snapshotController.SyncStockSnapshot(new[]
        {
            Snap("mat1", 1), Snap("mat2", 5), Snap("mat3", 1),
            Snap("comp1", 0), Snap("comp2", 0),
            Snap("prod1", 0), Snap("prod2", 0), Snap("prod3", 0),
        }, CancellationToken.None);

        await SyncMovementsAsync(new[]
        {
            Mov("mat1", +3), Mov("mat1", -2),   // net +1 => 2
            Mov("mat2", +1), Mov("mat2", -6),   // net -5 => 0
            Mov("mat3", +5), Mov("mat3", -2),   // net +3 => 4
        });

        var byArticle = await CalculateAsync();
        AssertScenario(byArticle);
    }

    private static void AssertScenario(Dictionary<string, decimal> byArticle)
    {
        Assert.Equal(0m, byArticle.GetValueOrDefault("prod1"));
        Assert.Equal(1m, byArticle.GetValueOrDefault("prod2"));
        Assert.Equal(2m, byArticle.GetValueOrDefault("prod3"));
    }

    private async Task SyncCompositionsAsync()
    {
        var controller = CreateRawController();
        var result = await controller.SyncProductCompositions(new[]
        {
            Comp("prod1", "comp1", 2), Comp("prod1", "comp2", 3), Comp("prod1", "mat1", 5),
            Comp("comp1", "mat2", 3), Comp("comp1", "comp2", 2),
            Comp("comp2", "mat3", 2), Comp("comp2", "mat1", 2),
            Comp("prod2", "comp2", 1),
            Comp("prod3", "mat3", 2),
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SyncRawDataResponse>(ok.Value);
        Assert.Equal(9, response.Count);
    }

    private async Task SyncMovementsAsync(StockMovementItem[] items)
    {
        var controller = CreateRawController();
        var payload = items.Length == 0
            ? new[] { Mov("mat1", 0) } // controller requires at least one item; zero-delta keeps effective stock unchanged
            : items;
        var result = await controller.SyncStockMovements(payload, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    private async Task<Dictionary<string, decimal>> CalculateAsync()
    {
        var service = new BalanceCalculationService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BalanceCalculationSettings { DefaultWarehouse = Wh }),
            NullLogger<BalanceCalculationService>.Instance);

        var result = await service.CalculateAsync(Wh, CancellationToken.None);
        return result.ToDictionary(x => x.Article, x => x.Balance);
    }

    private RawDataSyncController CreateRawController()
    {
        var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        // Keep scope/db alive through test by attaching to provider; simplest: use a non-disposed context from root for tests.
        // Instead, build controller from a scope-managed context and dispose scope after action in caller pattern is complex;
        // use a dedicated context bound to same DB path.
        scope.Dispose();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var ctx = new GatewayDbContext(options);

        var controller = new RawDataSyncController(
            ctx,
            Options.Create(new InternalApiSettings { SecretKey = Secret }),
            NullLogger<RawDataSyncController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[RawDataSyncController.SecretHeaderName] = Secret;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static ProductCompositionItem Comp(string product, string component, decimal qty) => new()
    {
        Warehouse = Wh,
        ProductArticle = product,
        ComponentArticle = component,
        QuantityPerUnit = qty
    };

    private static StockSnapshotItem Snap(string article, decimal inStock) => new()
    {
        SnapshotDate = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero),
        Warehouse = Wh,
        Article = article,
        Name = article,
        Unit = "шт",
        Type = article.StartsWith("prod", StringComparison.Ordinal) ? "Готовая продукция" : "Материал",
        InStock = inStock
    };

    private static StockMovementItem Mov(string article, decimal delta) => new()
    {
        Period = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero),
        Warehouse = Wh,
        Article = article,
        InStockDelta = delta
    };

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch { }
    }
}
