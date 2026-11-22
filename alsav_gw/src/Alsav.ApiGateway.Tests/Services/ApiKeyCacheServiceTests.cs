using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Data.Entities;
using Alsav.ApiGateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alsav.ApiGateway.Tests.Services;

public sealed class ApiKeyCacheServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;
    private readonly ApiKeyCacheService _service;

    public ApiKeyCacheServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"alsav-gw-apikey-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<GatewayDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();
        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.EnsureCreated();
        }

        _service = new ApiKeyCacheService(_provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ApiKeyCacheService>.Instance);
    }

    [Fact]
    public async Task StartAsync_LoadsKeysFromDb()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            db.ApiKeys.Add(new ApiKeyEntity { Key = "k1", ClientId = "c1", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await _service.StartAsync(CancellationToken.None);

        Assert.True(_service.IsValidKey("k1"));
        Assert.Equal(1, _service.KeyCount);
        Assert.NotNull(_service.GetLastSuccessfulSync());
    }

    [Fact]
    public async Task UpsertAndDelete_UpdateCacheAndDb()
    {
        await _service.StartAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;

        await _service.UpsertAsync([new ApiKeyEntity { Key = "k1", ClientId = "client-1", CreatedAt = now }]);
        Assert.True(_service.IsValidKey("k1"));
        Assert.Equal(1, _service.KeyCount);

        var all = await _service.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("client-1", all[0].ClientId);

        var deleted = await _service.DeleteAsync("k1");
        Assert.True(deleted);
        Assert.False(_service.IsValidKey("k1"));
        Assert.Equal(0, _service.KeyCount);
    }

    [Fact]
    public async Task ReplaceAll_ReplacesExistingKeys()
    {
        await _service.StartAsync(CancellationToken.None);
        await _service.UpsertAsync([new ApiKeyEntity { Key = "old", ClientId = "c1", CreatedAt = DateTimeOffset.UtcNow }]);

        await _service.ReplaceAllAsync([
            new ApiKeyEntity { Key = "new1", ClientId = "c1", CreatedAt = DateTimeOffset.UtcNow },
            new ApiKeyEntity { Key = "new2", ClientId = "c2", CreatedAt = DateTimeOffset.UtcNow }
        ]);

        Assert.False(_service.IsValidKey("old"));
        Assert.True(_service.IsValidKey("new1"));
        Assert.True(_service.IsValidKey("new2"));
        Assert.Equal(2, _service.KeyCount);
    }

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
