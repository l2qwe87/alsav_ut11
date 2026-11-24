using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alsav.ApiGateway.Controllers.External;

/// <summary>
/// Внешний API для получения актуальных остатков.
/// </summary>
[ApiController]
[Route("clientApi/stocks")]
[ApiExplorerSettings(GroupName = "external")]
public sealed class StocksController : ControllerBase
{
    private const int DefaultLimit = 1000;
    private const int MaxLimit = 5000;

    private readonly StockCacheService _stockCache;
    private readonly StockCalculationService _calculationService;
    private readonly ILogger<StocksController> _logger;

    public StocksController(
        StockCacheService stockCache,
        StockCalculationService calculationService,
        ILogger<StocksController> logger)
    {
        ArgumentNullException.ThrowIfNull(stockCache);
        ArgumentNullException.ThrowIfNull(calculationService);
        ArgumentNullException.ThrowIfNull(logger);

        _stockCache = stockCache;
        _calculationService = calculationService;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает остатки с фильтрацией и пагинацией.
    /// </summary>
    /// <param name="article">Фильтр по артикулу (поиск по подстроке, без учёта регистра).</param>
    /// <param name="barcode">Фильтр по штрихкоду (поиск по подстроке, без учёта регистра).</param>
    /// <param name="name">Фильтр по наименованию (поиск по подстроке, без учёта регистра).</param>
    /// <param name="limit">Размер страницы. По умолчанию 1000, максимум 5000.</param>
    /// <param name="offset">Смещение от начала выборки.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <returns>Страница остатков и общее количество позиций после фильтрации.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(StocksResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StocksResponse>> GetStocks(
        [FromQuery] string? article,
        [FromQuery] string? barcode,
        [FromQuery] string? name,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        offset = Math.Max(offset, 0);

        var useCalculated = await _calculationService.HasRawDataAsync(null, ct);
        IReadOnlyCollection<StockItem> allItems = useCalculated
            ? await _calculationService.CalculateAsync(null, ct)
            : _stockCache.GetAll();
        var lastUpdate = useCalculated ? DateTimeOffset.UtcNow : _stockCache.GetLastUpdateTime();

        var hasFilters = !string.IsNullOrWhiteSpace(article)
            || !string.IsNullOrWhiteSpace(barcode)
            || !string.IsNullOrWhiteSpace(name);

        IEnumerable<StockItem> source = allItems;
        var total = allItems.Count;

        if (hasFilters)
        {
            var filtered = Filter(allItems, article, barcode, name);
            source = filtered;
            total = filtered.Count;
        }

        var page = source.Skip(offset).Take(limit).ToArray();

        _logger.LogDebug(
            "Stocks requested. Calculated: {Calculated}, Total: {Total}, Returned: {Returned}, Offset: {Offset}, Limit: {Limit}.",
            useCalculated,
            total,
            page.Length,
            offset,
            limit);

        return Ok(new StocksResponse(page, total, lastUpdate));
    }

    private static List<StockItem> Filter(
        IReadOnlyCollection<StockItem> items,
        string? article,
        string? barcode,
        string? name)
    {
        var result = new List<StockItem>(items.Count);

        foreach (var item in items)
        {
            if (!Matches(item.Article, article))
            {
                continue;
            }

            if (!Matches(item.Barcode, barcode))
            {
                continue;
            }

            if (!Matches(item.Name, name))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static bool Matches(string value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
