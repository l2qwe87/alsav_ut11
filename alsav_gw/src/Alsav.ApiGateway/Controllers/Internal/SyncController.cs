using System.Security.Cryptography;
using System.Text;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Controllers.Internal;

/// <summary>
/// Внутренний API для приёма актуальных остатков из 1С.
/// </summary>
[ApiController]
[Route("api/internal/v1/sync")]
public sealed class SyncController : ControllerBase
{
    /// <summary>
    /// Имя заголовка с внутренним секретом.
    /// </summary>
    public const string SecretHeaderName = "X-Internal-Secret";

    private readonly BalanceCacheService _balanceCache;
    private readonly InternalApiSettings _settings;
    private readonly ILogger<SyncController> _logger;

    /// <summary>
    /// Создаёт экземпляр контроллера.
    /// </summary>
    /// <param name="balanceCache">Кэш остатков.</param>
    /// <param name="settings">Настройки внутреннего API.</param>
    /// <param name="logger">Логгер.</param>
    public SyncController(
        BalanceCacheService balanceCache,
        IOptions<InternalApiSettings> settings,
        ILogger<SyncController> logger)
    {
        ArgumentNullException.ThrowIfNull(balanceCache);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _balanceCache = balanceCache;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Принимает полный снимок остатков и атомарно заменяет кэш.
    /// </summary>
    /// <param name="items">Массив позиций остатков.</param>
    /// <returns>Результат синхронизации.</returns>
    [HttpPost("balances")]
    [ProducesResponseType(typeof(SyncBalancesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public IActionResult SyncBalances([FromBody] BalanceItem[]? items)
    {
        if (!IsSecretValid())
        {
            _logger.LogWarning(
                "Rejected balance sync request with invalid internal secret. RemoteIp: {RemoteIp}.",
                HttpContext.Connection.RemoteIpAddress);

            return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        }

        if (items is null || items.Length == 0)
        {
            return BadRequest(new ErrorResponse("Payload must contain at least one balance item"));
        }

        var validationErrors = ValidateItems(items);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new ErrorResponse("Validation failed", string.Join("; ", validationErrors)));
        }

        var startedAt = DateTimeOffset.UtcNow;
        _balanceCache.UpdateBalances(items);
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        _logger.LogInformation(
            "Balance cache updated. Items received: {Count}, Unique items: {UniqueCount}, DurationMs: {DurationMs}.",
            items.Length,
            _balanceCache.Count,
            elapsed.TotalMilliseconds);

        return Ok(new SyncBalancesResponse(true, items.Length, _balanceCache.GetLastUpdateTime()));
    }

    private bool IsSecretValid()
    {
        if (!Request.Headers.TryGetValue(SecretHeaderName, out var values) || values.Count == 0)
        {
            return false;
        }

        var provided = values[0];
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(_settings.SecretKey);

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static List<string> ValidateItems(IReadOnlyList<BalanceItem> items)
    {
        var errors = new List<string>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                errors.Add($"Item[{i}] is null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Article))
            {
                errors.Add($"Item[{i}].Article is required");
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add($"Item[{i}].Name is required");
            }

            if (string.IsNullOrWhiteSpace(item.Unit))
            {
                errors.Add($"Item[{i}].Unit is required");
            }

            if (string.IsNullOrWhiteSpace(item.Type))
            {
                errors.Add($"Item[{i}].Type is required");
            }
        }

        return errors;
    }
}
