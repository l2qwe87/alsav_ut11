using System.Security.Cryptography;
using System.Text;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data.Entities;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Controllers.Internal;

[ApiController]
[Route("api/internal/v1/api-keys")]
[ApiExplorerSettings(GroupName = "internal")]
public sealed class ApiKeysSyncController(
    ApiKeyCacheService cache,
    IOptions<InternalApiSettings> settings,
    ILogger<ApiKeysSyncController> logger) : ControllerBase
{
    public const string SecretHeaderName = "X-Internal-Secret";
    private readonly ApiKeyCacheService _cache = cache;
    private readonly InternalApiSettings _settings = settings.Value;
    private readonly ILogger<ApiKeysSyncController> _logger = logger;

    [HttpGet]
    [ProducesResponseType(typeof(ApiKeysResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        var items = await _cache.GetAllAsync(ct);
        var dto = items.Select(x => new ApiKeyItem { Key = x.Key, ClientId = x.ClientId, CreatedAt = x.CreatedAt }).ToArray();
        return Ok(new ApiKeysResponse(dto, dto.Length));
    }

    [HttpPost]
    [ProducesResponseType(typeof(SyncRawDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Upsert([FromBody] ApiKeyItem[]? items, CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        if (items is null || items.Length == 0) return BadRequest(new ErrorResponse("Payload must contain at least one item"));

        await _cache.UpsertAsync(items.Select(x => new ApiKeyEntity { Key = x.Key, ClientId = x.ClientId, CreatedAt = x.CreatedAt }), ct);
        _logger.LogInformation("API keys upserted. Count: {Count}.", items.Length);
        return Ok(new SyncRawDataResponse(true, items.Length, DateTimeOffset.UtcNow));
    }

    [HttpPut]
    [ProducesResponseType(typeof(SyncRawDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReplaceAll([FromBody] ApiKeyItem[]? items, CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        if (items is null) return BadRequest(new ErrorResponse("Payload must not be null"));

        await _cache.ReplaceAllAsync(items.Select(x => new ApiKeyEntity { Key = x.Key, ClientId = x.ClientId, CreatedAt = x.CreatedAt }), ct);
        _logger.LogInformation("API keys replaced. Count: {Count}.", items.Length);
        return Ok(new SyncRawDataResponse(true, items.Length, DateTimeOffset.UtcNow));
    }

    [HttpDelete]
    [ProducesResponseType(typeof(SyncRawDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromBody] ApiKeyDeleteRequest? request, CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        if (request is null || string.IsNullOrWhiteSpace(request.Key)) return BadRequest(new ErrorResponse("Key is required"));

        var removed = await _cache.DeleteAsync(request.Key, ct);
        if (!removed) return NotFound(new ErrorResponse("API key not found"));
        _logger.LogInformation("API key deleted.");
        return Ok(new SyncRawDataResponse(true, 1, DateTimeOffset.UtcNow));
    }

    private bool IsSecretValid()
    {
        if (!Request.Headers.TryGetValue(SecretHeaderName, out var values) || values.Count == 0) return false;
        var provided = values[0];
        if (string.IsNullOrEmpty(provided)) return false;
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(_settings.SecretKey);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
