using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Data.Entities;
using Alsav.ApiGateway.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Services;

public sealed class BalanceCalculationService(
    IServiceScopeFactory scopeFactory,
    IOptions<BalanceCalculationSettings> settings,
    ILogger<BalanceCalculationService> logger)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly BalanceCalculationSettings _settings = settings.Value;
    private readonly ILogger<BalanceCalculationService> _logger = logger;

    public async Task<bool> HasRawDataAsync(string? warehouse, CancellationToken ct)
    {
        var wh = string.IsNullOrWhiteSpace(warehouse) ? _settings.DefaultWarehouse : warehouse.Trim();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var hasSnapshots = await db.StockSnapshots.AsNoTracking().AnyAsync(x => x.Warehouse == wh, ct);
        if (!hasSnapshots) return false;
        return await db.StockMovements.AsNoTracking().AnyAsync(x => x.Warehouse == wh, ct);
    }

    public async Task<IReadOnlyList<BalanceItem>> CalculateAsync(string? warehouse, CancellationToken ct)
    {
        var wh = string.IsNullOrWhiteSpace(warehouse) ? _settings.DefaultWarehouse : warehouse.Trim();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var snapshots = await db.StockSnapshots.AsNoTracking()
            .Where(x => x.Warehouse == wh)
            .ToListAsync(ct);
        if (snapshots.Count == 0) return [];

        var movements = await db.StockMovements.AsNoTracking()
            .Where(x => x.Warehouse == wh)
            .ToListAsync(ct);
        var compositions = await db.ProductCompositions.AsNoTracking()
            .Where(x => x.Warehouse == wh)
            .ToListAsync(ct);

        var deltas = movements
            .GroupBy(x => Key(x.Article, x.Characteristic, x.Series))
            .ToDictionary(g => g.Key, g => new Delta(
                g.Sum(x => x.InStockDelta),
                g.Sum(x => x.ReservedInWarehouseDelta),
                g.Sum(x => x.ReservedForOrderDelta),
                g.Sum(x => x.PaidUnsecuredDemandDelta)));

        var available = new Dictionary<string, (decimal Available, decimal Demand)>(StringComparer.Ordinal);
        foreach (var s in snapshots)
        {
            var k = Key(s.Article, s.Characteristic, s.Series);
            deltas.TryGetValue(k, out var d);
            var inStock = s.InStock + d.InStock;
            var resWh = s.ReservedInWarehouse + d.ResWh;
            var resOrd = s.ReservedForOrder + d.ResOrd;
            var demand = s.PaidUnsecuredDemand + d.Demand;
            available[k] = (inStock - resWh - resOrd, demand);
        }

        var compsByProduct = compositions
            .Where(c => c.QuantityPerUnit > 0)
            .GroupBy(c => Key(c.ProductArticle, c.ProductCharacteristic, c.ProductSeries))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<BalanceItem>(snapshots.Count);
        foreach (var s in snapshots)
        {
            var pk = Key(s.Article, s.Characteristic, s.Series);
            var canBuild = CanBuild(pk, available, compsByProduct);
            if (canBuild < 0) canBuild = 0;

            result.Add(new BalanceItem
            {
                Series = s.Series,
                Barcode = s.Barcode,
                Article = s.Article,
                Unit = s.Unit,
                Name = s.Name,
                Characteristic = s.Characteristic,
                Price = s.Price,
                Type = s.Type,
                Balance = canBuild
            });
        }

        _logger.LogDebug("Calculated balances for warehouse {Warehouse}: {Count} items.", wh, result.Count);
        return result;
    }

    private static decimal CanBuild(
        string productKey,
        Dictionary<string, (decimal Available, decimal Demand)> available,
        Dictionary<string, List<ProductCompositionEntity>> compsByProduct)
    {
        var memo = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        return CanBuild(productKey, available, compsByProduct, memo, visiting);
    }

    private static decimal CanBuild(
        string productKey,
        Dictionary<string, (decimal Available, decimal Demand)> available,
        Dictionary<string, List<ProductCompositionEntity>> compsByProduct,
        Dictionary<string, decimal> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(productKey, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(productKey))
        {
            return 0m;
        }

        available.TryGetValue(productKey, out var self);
        var selfAvailable = self.Available;
        var selfDemand = self.Demand;

        decimal result;
        if (compsByProduct.TryGetValue(productKey, out var components) && components.Count > 0)
        {
            decimal? minBuild = null;
            foreach (var c in components)
            {
                var ck = Key(c.ComponentArticle, c.ComponentCharacteristic, c.ComponentSeries);
                var componentEffective = GetEffectiveAvailable(ck, available, compsByProduct, memo, visiting);
                var perUnit = c.QuantityPerUnit;
                var can = perUnit <= 0 ? 0m : Math.Floor((componentEffective - GetDemand(ck, available)) / perUnit);
                if (can < 0) can = 0;
                minBuild = minBuild.HasValue ? Math.Min(minBuild.Value, can) : can;
            }

            var fromComponents = minBuild ?? 0m;
            var fromSelf = Math.Floor(selfAvailable - selfDemand);
            if (fromSelf < 0) fromSelf = 0;
            result = fromSelf + fromComponents;
        }
        else
        {
            result = Math.Floor(selfAvailable - selfDemand);
            if (result < 0) result = 0;
        }

        visiting.Remove(productKey);
        memo[productKey] = result;
        return result;
    }

    private static decimal GetEffectiveAvailable(
        string key,
        Dictionary<string, (decimal Available, decimal Demand)> available,
        Dictionary<string, List<ProductCompositionEntity>> compsByProduct,
        Dictionary<string, decimal> memo,
        HashSet<string> visiting)
    {
        available.TryGetValue(key, out var self);
        if (compsByProduct.TryGetValue(key, out var components) && components.Count > 0)
        {
            decimal? minBuild = null;
            foreach (var c in components)
            {
                var ck = Key(c.ComponentArticle, c.ComponentCharacteristic, c.ComponentSeries);
                var componentEffective = GetEffectiveAvailable(ck, available, compsByProduct, memo, visiting);
                var perUnit = c.QuantityPerUnit;
                var can = perUnit <= 0 ? 0m : Math.Floor((componentEffective - GetDemand(ck, available)) / perUnit);
                if (can < 0) can = 0;
                minBuild = minBuild.HasValue ? Math.Min(minBuild.Value, can) : can;
            }

            var fromComponents = minBuild ?? 0m;
            return self.Available + fromComponents;
        }

        return self.Available;
    }

    private static decimal GetDemand(string key, Dictionary<string, (decimal Available, decimal Demand)> available)
    {
        available.TryGetValue(key, out var self);
        return self.Demand;
    }

    private static string Key(string a, string c, string s) => $"{a}|{c}|{s}".Trim('|');

    private readonly record struct Delta(decimal InStock, decimal ResWh, decimal ResOrd, decimal Demand);
}
