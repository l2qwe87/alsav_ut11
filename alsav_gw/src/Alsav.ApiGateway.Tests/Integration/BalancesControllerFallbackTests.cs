using System.Net;
using System.Net.Http.Json;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Data.Entities;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alsav.ApiGateway.Tests.Integration;

public sealed class BalancesControllerFallbackTests
{
    private static WebApplicationFactory<Program> CreateFactory(string dbPath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:GatewayDb", $"Data Source={dbPath}");
            builder.UseSetting("InternalApi:SecretKey", "test-secret");
            builder.UseSetting("BalanceSeed:Enabled", "false");
            builder.UseSetting("BalanceCalculation:DefaultWarehouse", "WH");
        });

    [Fact]
    public async Task GetBalances_UsesPreparedCache_WhenRawDataMissing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-bal-fallback-{Guid.NewGuid():N}.db");
        await using var factory = CreateFactory(dbPath);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            db.Database.EnsureCreated();
            db.StockSnapshots.RemoveRange(db.StockSnapshots);
            db.StockMovements.RemoveRange(db.StockMovements);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<BalanceCacheService>().UpdateBalances([
                new BalanceItem { Series = "S", Type = "T", Article = "A-CACHE", Name = "Cached", Unit = "шт", Balance = 5 }
            ]);
        }

        await SeedApiKeyAsync(factory, "k-cache");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "k-cache");

        var resp = await client.GetAsync("/clientApi/balances?article=A-CACHE&limit=10&offset=0");
        var body = await resp.Content.ReadFromJsonAsync<BalancesResponse>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(body!.Data, x => x.Article == "A-CACHE" && x.Name == "Cached" && x.Balance == 5);
    }

    [Fact]
    public async Task GetBalances_UsesCalculated_WhenRawDataPresent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-bal-calc-{Guid.NewGuid():N}.db");
        await using var factory = CreateFactory(dbPath);
        var snapDate = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            db.Database.EnsureCreated();
            db.StockSnapshots.RemoveRange(db.StockSnapshots);
            db.StockMovements.RemoveRange(db.StockMovements);
            db.StockSnapshots.Add(new StockSnapshotEntity
            {
                SnapshotDate = snapDate, Warehouse = "WH", Article = "A-CALC", Characteristic = "", Series = "",
                Barcode = "", Name = "Raw", Unit = "шт", Type = "Товар", Price = 0,
                InStock = 10, ReservedInWarehouse = 0, ReservedForOrder = 0, PaidUnsecuredDemand = 0
            });
            db.StockMovements.Add(new StockMovementEntity
            {
                Period = snapDate.AddHours(1), Warehouse = "WH", Article = "A-CALC", Characteristic = "", Series = "",
                InStockDelta = -3, ReservedInWarehouseDelta = 0, ReservedForOrderDelta = 0, PaidUnsecuredDemandDelta = 0
            });
            await db.SaveChangesAsync();

            scope.ServiceProvider.GetRequiredService<BalanceCacheService>().UpdateBalances([
                new BalanceItem { Series = "S", Type = "T", Article = "A-CALC", Name = "Cached", Unit = "шт", Balance = 999 }
            ]);
        }

        await SeedApiKeyAsync(factory, "k-calc");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "k-calc");

        var resp = await client.GetAsync("/clientApi/balances?article=A-CALC&limit=10&offset=0");
        var body = await resp.Content.ReadFromJsonAsync<BalancesResponse>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(body!.Data, x => x.Article == "A-CALC" && x.Name == "Raw" && x.Balance == 7);
    }

    private static async Task SeedApiKeyAsync(WebApplicationFactory<Program> factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Secret", "test-secret");
        var resp = await client.PostAsJsonAsync("/api/internal/v1/api-keys", new[] { new { key, clientId = "c1", createdAt = DateTimeOffset.UtcNow } });
        resp.EnsureSuccessStatusCode();
    }
}
