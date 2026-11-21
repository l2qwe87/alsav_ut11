using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Controllers.Internal;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Tests.Controllers;

public sealed class RawDataSyncControllerTests : IDisposable
{
    private const string Secret = "test-internal-secret";
    private readonly string _dbPath;
    private readonly GatewayDbContext _db;

    public RawDataSyncControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"alsav-gw-rawsync-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new GatewayDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task SyncProductCompositions_WithValidSecret_ReplacesWarehouseData()
    {
        var controller = CreateController();
        var items = new[]
        {
            new ProductCompositionItem
            {
                Warehouse = "WH",
                ProductArticle = "prod1",
                ComponentArticle = "comp1",
                QuantityPerUnit = 2
            },
            new ProductCompositionItem
            {
                Warehouse = "WH",
                ProductArticle = "prod1",
                ComponentArticle = "mat1",
                QuantityPerUnit = 5
            }
        };

        var result = await controller.SyncProductCompositions(items, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SyncRawDataResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(2, response.Count);
        Assert.Equal(2, await _db.ProductCompositions.CountAsync());
        Assert.All(await _db.ProductCompositions.ToListAsync(), x => Assert.Equal("WH", x.Warehouse));
    }

    [Fact]
    public async Task SyncStockSnapshot_WithValidSecret_ReplacesWarehouseDataAndClearsMovements()
    {
        _db.StockMovements.Add(new Data.Entities.StockMovementEntity
        {
            Warehouse = "WH",
            Article = "mat1",
            Period = DateTimeOffset.UtcNow,
            InStockDelta = 1
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var items = new[]
        {
            new StockSnapshotItem
            {
                SnapshotDate = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero),
                Warehouse = "WH",
                Article = "mat1",
                Name = "Материал 1",
                InStock = 10
            }
        };

        var result = await controller.SyncStockSnapshot(items, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SyncRawDataResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(1, response.Count);
        Assert.Single(await _db.StockSnapshots.ToListAsync());
        Assert.Empty(await _db.StockMovements.ToListAsync());
        var saved = await _db.StockSnapshots.SingleAsync();
        Assert.Equal("mat1", saved.Article);
        Assert.Equal(10m, saved.InStock);
    }

    [Fact]
    public async Task SyncStockMovements_WithValidSecret_ReplacesWarehouseData()
    {
        var controller = CreateController();
        var items = new[]
        {
            new StockMovementItem
            {
                Period = new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero),
                Warehouse = "WH",
                Article = "mat1",
                InStockDelta = 5
            },
            new StockMovementItem
            {
                Period = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero),
                Warehouse = "WH",
                Article = "mat1",
                InStockDelta = -2
            }
        };

        var result = await controller.SyncStockMovements(items, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SyncRawDataResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(2, response.Count);
        Assert.Equal(2, await _db.StockMovements.CountAsync());
        Assert.Equal(3m, (await _db.StockMovements.ToListAsync()).Sum(x => x.InStockDelta));
    }

    [Fact]
    public async Task SyncEndpoints_WithInvalidSecret_ReturnUnauthorized()
    {
        var controller = CreateController(providedSecret: "wrong");
        var snap = await controller.SyncStockSnapshot([new StockSnapshotItem { SnapshotDate = DateTimeOffset.UtcNow, Article = "a", Name = "n" }], CancellationToken.None);
        var mov = await controller.SyncStockMovements([new StockMovementItem { Period = DateTimeOffset.UtcNow, Article = "a" }], CancellationToken.None);
        var comp = await controller.SyncProductCompositions([new ProductCompositionItem { ProductArticle = "p", ComponentArticle = "c", QuantityPerUnit = 1 }], CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(snap);
        Assert.IsType<UnauthorizedObjectResult>(mov);
        Assert.IsType<UnauthorizedObjectResult>(comp);
    }

    private RawDataSyncController CreateController(string? providedSecret = Secret)
    {
        var controller = new RawDataSyncController(
            _db,
            Options.Create(new InternalApiSettings { SecretKey = Secret }),
            NullLogger<RawDataSyncController>.Instance);

        var httpContext = new DefaultHttpContext();
        if (providedSecret is not null)
        {
            httpContext.Request.Headers[RawDataSyncController.SecretHeaderName] = providedSecret;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    public void Dispose()
    {
        _db.Dispose();
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
