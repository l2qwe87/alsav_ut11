using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;

namespace Alsav.ApiGateway.Tests.Services;

public sealed class BalanceCacheServiceTests
{
    private static BalanceItem Item(
        string article,
        string characteristic = "",
        string series = "",
        decimal balance = 1)
    {
        return new BalanceItem
        {
            Article = article,
            Characteristic = characteristic,
            Series = series,
            Balance = balance,
            Name = "Test product",
            Unit = "pcs",
            Type = "Product",
            Price = 100,
            Barcode = "4600000000001"
        };
    }

    [Fact]
    public void UpdateBalances_ReplacesWholeCache()
    {
        var service = new BalanceCacheService();
        service.UpdateBalances(new[] { Item("A"), Item("B") });

        service.UpdateBalances(new[] { Item("C") });

        Assert.Single(service.GetAll());
        Assert.NotNull(service.GetByKey("C"));
        Assert.Null(service.GetByKey("A"));
    }

    [Fact]
    public void UpdateBalances_WithEmptyCollection_ClearsCache()
    {
        var service = new BalanceCacheService();
        service.UpdateBalances(new[] { Item("A") });

        service.UpdateBalances(Array.Empty<BalanceItem>());

        Assert.Empty(service.GetAll());
        Assert.NotEqual(DateTimeOffset.MinValue, service.GetLastUpdateTime());
    }

    [Fact]
    public void UpdateBalances_ThrowsOnNull()
    {
        var service = new BalanceCacheService();

        Assert.Throws<ArgumentNullException>(() => service.UpdateBalances(null!));
    }

    [Fact]
    public void GetByKey_ReturnsItem_ByCompositeKey()
    {
        var service = new BalanceCacheService();
        service.UpdateBalances(new[] { Item("ART-1", "CHAR-1", "SER-1") });

        var result = service.GetByKey("ART-1|CHAR-1|SER-1");

        Assert.NotNull(result);
        Assert.Equal("ART-1", result!.Article);
    }

    [Fact]
    public void GetByKey_ReturnsNull_WhenKeyMissing()
    {
        var service = new BalanceCacheService();

        Assert.Null(service.GetByKey("missing"));
        Assert.Null(service.GetByKey(string.Empty));
    }

    [Fact]
    public void GetLastUpdateTime_IsMinValue_BeforeFirstUpdate()
    {
        var service = new BalanceCacheService();

        Assert.Equal(DateTimeOffset.MinValue, service.GetLastUpdateTime());
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public async Task UpdateBalances_IsThreadSafe_UnderConcurrentReadsAndWrites()
    {
        var service = new BalanceCacheService();
        service.UpdateBalances(new[] { Item("A") });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writer = Task.Run(async () =>
        {
            var index = 0;
            while (!cancellation.IsCancellationRequested)
            {
                service.UpdateBalances(new[] { Item($"A{index}"), Item($"B{index}") });
                index++;
                await Task.Yield();
            }
        });

        var reader = Task.Run(() =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                var snapshot = service.GetAll();
                Assert.True(snapshot.Count is 1 or 2);
            }
        });

        await Task.WhenAll(writer, reader);
    }
}
