using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Data.Entities;
using Alsav.ApiGateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Tests.Services;

public sealed class StockCalculationIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public StockCalculationIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"alsav-gw-test-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<GatewayDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddLogging();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task CalculateAsync_RecursiveComposition_ReturnsIndependentBuildableQtyPerProduct()
    {
        const string wh = "Склад адресного хранения";
        var snapshotDate = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            db.ProductCompositions.AddRange(
                Comp(wh, "prod1", "comp1", 2),
                Comp(wh, "prod1", "comp2", 3),
                Comp(wh, "prod1", "mat1", 5),
                Comp(wh, "comp1", "mat2", 3),
                Comp(wh, "comp1", "comp2", 2),
                Comp(wh, "comp2", "mat3", 2),
                Comp(wh, "comp2", "mat1", 2),
                Comp(wh, "prod2", "comp2", 1),
                Comp(wh, "prod3", "mat3", 2));

            db.StockSnapshots.AddRange(
                Snap(snapshotDate, wh, "mat1", inStock: 2),
                Snap(snapshotDate, wh, "mat2", inStock: 0),
                Snap(snapshotDate, wh, "mat3", inStock: 4),
                Snap(snapshotDate, wh, "comp1", inStock: 0),
                Snap(snapshotDate, wh, "comp2", inStock: 0),
                Snap(snapshotDate, wh, "prod1", inStock: 0),
                Snap(snapshotDate, wh, "prod2", inStock: 0),
                Snap(snapshotDate, wh, "prod3", inStock: 0));

            await db.SaveChangesAsync();
        }

        var service = new StockCalculationService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new StockCalculationSettings { DefaultWarehouse = wh }),
            NullLogger<StockCalculationService>.Instance);

        var result = await service.CalculateAsync(wh, CancellationToken.None);
        var byArticle = result.ToDictionary(x => x.Article, x => x.Stock);

        Assert.Equal(0m, byArticle["prod1"]);
        Assert.Equal(1m, byArticle["prod2"]);
        Assert.Equal(2m, byArticle["prod3"]);
    }

    private static ProductCompositionEntity Comp(string wh, string product, string component, decimal qty) => new()
    {
        Warehouse = wh,
        ProductArticle = product,
        ProductCharacteristic = string.Empty,
        ProductSeries = string.Empty,
        ComponentArticle = component,
        ComponentCharacteristic = string.Empty,
        ComponentSeries = string.Empty,
        QuantityPerUnit = qty
    };

    private static StockSnapshotEntity Snap(DateTimeOffset date, string wh, string article, decimal inStock) => new()
    {
        SnapshotDate = date,
        Warehouse = wh,
        Article = article,
        Characteristic = string.Empty,
        Series = string.Empty,
        Barcode = string.Empty,
        Name = article,
        Unit = "шт",
        Type = article.StartsWith("prod", StringComparison.Ordinal) ? "Готовая продукция" : "Материал",
        Price = 0,
        InStock = inStock,
        ReservedInWarehouse = 0,
        ReservedForOrder = 0,
        PaidUnsecuredDemand = 0
    };

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            var wal = _dbPath + "-wal";
            var shm = _dbPath + "-shm";
            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch
        {
        }
    }
}
