using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;

namespace Alsav.ApiGateway.Tests.Services;

public sealed class StockCacheServiceTests
{
    private static StockItem Item(
        string article,
        string characteristic = "",
        string series = "",
        decimal stock = 1)
    {
        return new StockItem
        {
            Article = article,
            Characteristic = characteristic,
            Series = series,
            Stock = stock,
            Name = "Test product",
            Unit = "pcs",
            Type = "Product",
            Price = 100,
            Barcode = "4600000000001"
        };
    }

    [Fact]
    public void UpdateStocks_ReplacesWholeCache()
    {
        var service = new StockCacheService();
        service.UpdateStocks(new[] { Item("A"), Item("B") });

        service.UpdateStocks(new[] { Item("C") });

        Assert.Single(service.GetAll());
        Assert.NotNull(service.GetByKey("C"));
        Assert.Null(service.GetByKey("A"));
    }

    [Fact]
    public void UpdateStocks_WithEmptyCollection_ClearsCache()
    {
        var service = new StockCacheService();
        service.UpdateStocks(new[] { Item("A") });

        service.UpdateStocks(Array.Empty<StockItem>());

        Assert.Empty(service.GetAll());
        Assert.NotEqual(DateTimeOffset.MinValue, service.GetLastUpdateTime());
    }

    [Fact]
    public void UpdateStocks_ThrowsOnNull()
    {
        var service = new StockCacheService();

        Assert.Throws<ArgumentNullException>(() => service.UpdateStocks(null!));
    }

    [Fact]
    public void GetByKey_ReturnsItem_ByCompositeKey()
    {
        var service = new StockCacheService();
        service.UpdateStocks(new[] { Item("ART-1", "CHAR-1", "SER-1") });

        var result = service.GetByKey("ART-1|CHAR-1|SER-1");

        Assert.NotNull(result);
        Assert.Equal("ART-1", result!.Article);
    }

    [Fact]
    public void GetByKey_ReturnsNull_WhenKeyMissing()
    {
        var service = new StockCacheService();

        Assert.Null(service.GetByKey("missing"));
        Assert.Null(service.GetByKey(string.Empty));
    }

    [Fact]
    public void GetLastUpdateTime_IsMinValue_BeforeFirstUpdate()
    {
        var service = new StockCacheService();

        Assert.Equal(DateTimeOffset.MinValue, service.GetLastUpdateTime());
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public async Task UpdateStocks_IsThreadSafe_UnderConcurrentReadsAndWrites()
    {
        var service = new StockCacheService();
        service.UpdateStocks(new[] { Item("A") });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writer = Task.Run(async () =>
        {
            var index = 0;
            while (!cancellation.IsCancellationRequested)
            {
                service.UpdateStocks(new[] { Item($"A{index}"), Item($"B{index}") });
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
