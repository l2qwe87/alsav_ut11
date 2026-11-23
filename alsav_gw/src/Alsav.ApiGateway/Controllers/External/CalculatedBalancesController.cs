using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alsav.ApiGateway.Controllers.External;

[ApiController]
[Route("clientApi/balances/calculated")]
[ApiExplorerSettings(GroupName = "external")]
public sealed class CalculatedBalancesController(
    BalanceCalculationService calculationService,
    ILogger<CalculatedBalancesController> logger) : ControllerBase
{
    private const int DefaultLimit = 1000;
    private const int MaxLimit = 5000;
    private readonly BalanceCalculationService _calculationService = calculationService;
    private readonly ILogger<CalculatedBalancesController> _logger = logger;

    [HttpGet]
    [ProducesResponseType(typeof(BalancesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BalancesResponse>> Get(
        [FromQuery] string? article,
        [FromQuery] string? barcode,
        [FromQuery] string? name,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        offset = Math.Max(offset, 0);

        var allItems = await _calculationService.CalculateAsync(null, ct);
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
        _logger.LogDebug("Calculated balances requested. Total: {Total}, Returned: {Returned}.", total, page.Length);
        return Ok(new BalancesResponse(page, total, DateTimeOffset.UtcNow));
    }

    private static List<BalanceItem> Filter(IReadOnlyCollection<BalanceItem> items, string? article, string? barcode, string? name)
    {
        var result = new List<BalanceItem>(items.Count);
        foreach (var item in items)
        {
            if (!Matches(item.Article, article) || !Matches(item.Barcode, barcode) || !Matches(item.Name, name)) continue;
            result.Add(item);
        }
        return result;
    }

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
