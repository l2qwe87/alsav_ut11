using System.Collections.Immutable;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Alsav.ApiGateway.Services;

/// <summary>
/// Кэш и хранилище API-ключей клиентов. Ключи загружаются из БД при старте и обновляются через sync-эндпоинты.
/// </summary>
public class ApiKeyCacheService(IServiceScopeFactory scopeFactory, ILogger<ApiKeyCacheService> logger) : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ApiKeyCacheService> _logger = logger;

    private ImmutableHashSet<string> _keys = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
    private long _lastSuccessfulSyncTicks;

    public int KeyCount => Volatile.Read(ref _keys).Count;

    public DateTimeOffset? GetLastSuccessfulSync()
    {
        var ticks = Interlocked.Read(ref _lastSuccessfulSyncTicks);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public virtual bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return Volatile.Read(ref _keys).Contains(key.Trim());
    }

    public async Task<IReadOnlyList<ApiKeyEntity>> GetAllAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var items = await db.ApiKeys.AsNoTracking().ToListAsync(ct);
        return items.OrderBy(x => x.ClientId, StringComparer.Ordinal)
            .ThenBy(x => x.CreatedAt)
            .ToList();
    }

    public async Task UpsertAsync(IEnumerable<ApiKeyEntity> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        foreach (var item in items)
        {
            var key = item.Key?.Trim() ?? string.Empty;
            var clientId = item.ClientId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(clientId)) continue;

            var existing = await db.ApiKeys.FirstOrDefaultAsync(x => x.Key == key, ct);
            if (existing is null)
            {
                db.ApiKeys.Add(new ApiKeyEntity { Key = key, ClientId = clientId, CreatedAt = item.CreatedAt });
            }
            else
            {
                existing.ClientId = clientId;
                existing.CreatedAt = item.CreatedAt;
            }
        }

        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = key.Trim();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var existing = await db.ApiKeys.FirstOrDefaultAsync(x => x.Key == normalized, ct);
        if (existing is null) return false;

        db.ApiKeys.Remove(existing);
        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(ct);
        return true;
    }

    public async Task ReplaceAllAsync(IEnumerable<ApiKeyEntity> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        await db.ApiKeys.ExecuteDeleteAsync(ct);
        var entities = items
            .Select(x => new ApiKeyEntity
            {
                Key = x.Key?.Trim() ?? string.Empty,
                ClientId = x.ClientId?.Trim() ?? string.Empty,
                CreatedAt = x.CreatedAt
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.ClientId))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();

        if (entities.Count > 0)
        {
            db.ApiKeys.AddRange(entities);
            await db.SaveChangesAsync(ct);
        }

        await ReloadCacheAsync(ct);
    }

    public async Task ReloadCacheAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var keys = await db.ApiKeys.AsNoTracking().Select(x => x.Key).ToListAsync(ct);
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key)) builder.Add(key.Trim());
        }

        Volatile.Write(ref _keys, builder.ToImmutable());
        Interlocked.Exchange(ref _lastSuccessfulSyncTicks, DateTime.UtcNow.Ticks);
        _logger.LogInformation("API keys cache loaded from DB. Keys: {Count}.", KeyCount);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadCacheAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
