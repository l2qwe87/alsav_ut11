using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alsav.ApiGateway.Controllers;

/// <summary>
/// Endpoint для мониторинга состояния сервиса и кэшей.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly BalanceCacheService _balanceCache;
    private readonly ApiKeyCacheService _apiKeyCache;

    /// <summary>
    /// Создаёт экземпляр контроллера.
    /// </summary>
    /// <param name="balanceCache">Кэш остатков.</param>
    /// <param name="apiKeyCache">Кэш API-ключей.</param>
    public HealthController(
        BalanceCacheService balanceCache,
        ApiKeyCacheService apiKeyCache)
    {
        ArgumentNullException.ThrowIfNull(balanceCache);
        ArgumentNullException.ThrowIfNull(apiKeyCache);

        _balanceCache = balanceCache;
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
        var balancesLastUpdate = _balanceCache.GetLastUpdateTime();

        return Ok(new HealthResponse(
            "Healthy",
            balancesLastUpdate == DateTimeOffset.MinValue ? null : balancesLastUpdate,
            _balanceCache.Count,
            _apiKeyCache.GetLastSuccessfulSync(),
            _apiKeyCache.KeyCount));
    }
}
