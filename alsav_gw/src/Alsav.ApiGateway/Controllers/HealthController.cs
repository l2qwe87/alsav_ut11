using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alsav.ApiGateway.Controllers;

/// <summary>
/// Endpoint для мониторинга состояния сервиса и кэшей.
/// </summary>
[ApiController]
[Route("health")]
[ApiExplorerSettings(GroupName = "internal")]
public sealed class HealthController : ControllerBase
{
    private readonly StockCacheService _stockCache;
    private readonly ApiKeyCacheService _apiKeyCache;

    /// <summary>
    /// Создаёт экземпляр контроллера.
    /// </summary>
    /// <param name="stockCache">Кэш остатков.</param>
    /// <param name="apiKeyCache">Кэш API-ключей.</param>
    public HealthController(
        StockCacheService stockCache,
        ApiKeyCacheService apiKeyCache)
    {
        ArgumentNullException.ThrowIfNull(stockCache);
        ArgumentNullException.ThrowIfNull(apiKeyCache);

        _stockCache = stockCache;
        _apiKeyCache = apiKeyCache;
    }

    /// <summary>
    /// Возвращает текущее состояние кэшей.
    /// </summary>
    /// <returns>Статус сервиса и метрики кэшей.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        var stocksLastUpdate = _stockCache.GetLastUpdateTime();

        return Ok(new HealthResponse(
            "Healthy",
            stocksLastUpdate == DateTimeOffset.MinValue ? null : stocksLastUpdate,
            _stockCache.Count,
            _apiKeyCache.GetLastSuccessfulSync(),
            _apiKeyCache.KeyCount));
    }
}
