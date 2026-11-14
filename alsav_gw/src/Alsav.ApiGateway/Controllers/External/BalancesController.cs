using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alsav.ApiGateway.Controllers.External;

/// <summary>
/// Внешний API для получения актуальных остатков из кэша.
/// </summary>
[ApiController]
[Route("clientApi/balances")]
public sealed class BalancesController : ControllerBase
{
    private const int DefaultLimit = 1000;
    private const int MaxLimit = 5000;

    private readonly BalanceCacheService _balanceCache;
    private readonly ILogger<BalancesController> _logger;

    /// <summary>
    /// Создаёт экземпляр контроллера.
    /// </summary>
    /// <param name="balanceCache">Кэш остатков.</param>
    /// <param name="logger">Логгер.</param>
    public BalancesController(
        BalanceCacheService balanceCache,
        ILogger<BalancesController> logger)
    {
        ArgumentNullException.ThrowIfNull(balanceCache);
        ArgumentNullException.ThrowIfNull(logger);

        _balanceCache = balanceCache;
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
    /// <returns>Страница остатков и общее количество позиций после фильтрации.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(BalancesResponse), StatusCodes.Status200OK)]
    public ActionResult<BalancesResponse> GetBalances(
        [FromQuery] string? article,
        [FromQuery] string? barcode,
        [FromQuery] string? name,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        offset = Math.Max(offset, 0);

        var allItems = _balanceCache.GetAll();
        var hasFilters = !string.IsNullOrWhiteSpace(article)
            || !string.IsNullOrWhiteSpace(barcode)
            || !string.IsNullOrWhiteSpace(name);

        IEnumerable<BalanceItem> source = allItems;
        var total = allItems.Count;

        if (hasFilters)
        {
            var filtered = Filter(allItems, article, barcode, name);
            source = filtered;
            total = filtered.Count;
        }

        var page = source.Skip(offset).Take(limit).ToArray();

        _logger.LogDebug(
            "Balances requested. Total: {Total}, Returned: {Returned}, Offset: {Offset}, Limit: {Limit}.",
            total,
            page.Length,
            offset,
            limit);

        return Ok(new BalancesResponse(page, total, _balanceCache.GetLastUpdateTime()));
    }

    private static List<BalanceItem> Filter(
        IReadOnlyCollection<BalanceItem> items,
        string? article,
        string? barcode,
        string? name)
    {
        var result = new List<BalanceItem>(items.Count);

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
