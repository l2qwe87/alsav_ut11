using System.Security.Cryptography;
using System.Text;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Data.Entities;
using Alsav.ApiGateway.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Controllers.Internal;

[ApiController]
[Route("api/internal/v1/sync")]
[ApiExplorerSettings(GroupName = "internal")]
public sealed class RawDataSyncController(
    GatewayDbContext db,
    IOptions<InternalApiSettings> settings,
    ILogger<RawDataSyncController> logger) : ControllerBase
{
    public const string SecretHeaderName = "X-Internal-Secret";
    private readonly GatewayDbContext _db = db;
    private readonly InternalApiSettings _settings = settings.Value;
    private readonly ILogger<RawDataSyncController> _logger = logger;

    [HttpPost("stock-snapshot")]
    [ProducesResponseType(typeof(SyncRawDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SyncStockSnapshot([FromBody] StockSnapshotItem[]? items, CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        if (items is null || items.Length == 0) return BadRequest(new ErrorResponse("Payload must contain at least one item"));

        var warehouse = items[0].Warehouse?.Trim() ?? string.Empty;
        var snapshotDate = items[0].SnapshotDate;

        await _db.StockMovements.Where(x => x.Warehouse == warehouse).ExecuteDeleteAsync(ct);
        await _db.StockSnapshots.Where(x => x.Warehouse == warehouse).ExecuteDeleteAsync(ct);

        var entities = items.Select(x => new StockSnapshotEntity
        {
            SnapshotDate = x.SnapshotDate,
            Warehouse = x.Warehouse?.Trim() ?? string.Empty,
            Series = x.Series?.Trim() ?? string.Empty,
            Barcode = x.Barcode?.Trim() ?? string.Empty,
            Article = x.Article?.Trim() ?? string.Empty,
            Unit = x.Unit?.Trim() ?? string.Empty,
            Name = x.Name?.Trim() ?? string.Empty,
            Characteristic = x.Characteristic?.Trim() ?? string.Empty,
            Price = x.Price,
            Type = x.Type?.Trim() ?? string.Empty,
            InStock = x.InStock,
            ReservedInWarehouse = x.ReservedInWarehouse,
            ReservedForOrder = x.ReservedForOrder,
            PaidUnsecuredDemand = x.PaidUnsecuredDemand,
        }).ToList();

        _db.StockSnapshots.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Stock snapshot synced. Warehouse: {Warehouse}, Count: {Count}, SnapshotDate: {SnapshotDate}.", warehouse, entities.Count, snapshotDate);
        return Ok(new SyncRawDataResponse(true, entities.Count, DateTimeOffset.UtcNow));
    }

    [HttpPost("stock-movements")]
    [ProducesResponseType(typeof(SyncRawDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SyncStockMovements([FromBody] StockMovementItem[]? items, CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        if (items is null || items.Length == 0) return BadRequest(new ErrorResponse("Payload must contain at least one item"));

        var warehouse = items[0].Warehouse?.Trim() ?? string.Empty;
        await _db.StockMovements.Where(x => x.Warehouse == warehouse).ExecuteDeleteAsync(ct);

        var entities = items.Select(x => new StockMovementEntity
        {
            Period = x.Period,
            Warehouse = x.Warehouse?.Trim() ?? string.Empty,
            Article = x.Article?.Trim() ?? string.Empty,
            Characteristic = x.Characteristic?.Trim() ?? string.Empty,
            Series = x.Series?.Trim() ?? string.Empty,
            InStockDelta = x.InStockDelta,
            ReservedInWarehouseDelta = x.ReservedInWarehouseDelta,
            ReservedForOrderDelta = x.ReservedForOrderDelta,
            PaidUnsecuredDemandDelta = x.PaidUnsecuredDemandDelta,
        }).ToList();

        _db.StockMovements.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Stock movements synced. Warehouse: {Warehouse}, Count: {Count}.", warehouse, entities.Count);
        return Ok(new SyncRawDataResponse(true, entities.Count, DateTimeOffset.UtcNow));
    }

    [HttpPost("product-compositions")]
    [ProducesResponseType(typeof(SyncRawDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SyncProductCompositions([FromBody] ProductCompositionItem[]? items, CancellationToken ct)
    {
        if (!IsSecretValid()) return Unauthorized(new ErrorResponse("Invalid or missing X-Internal-Secret header"));
        if (items is null || items.Length == 0) return BadRequest(new ErrorResponse("Payload must contain at least one item"));

        var warehouse = items[0].Warehouse?.Trim() ?? string.Empty;
        await _db.ProductCompositions.Where(x => x.Warehouse == warehouse).ExecuteDeleteAsync(ct);

        var entities = items.Select(x => new ProductCompositionEntity
        {
            ProductArticle = x.ProductArticle?.Trim() ?? string.Empty,
            ProductCharacteristic = x.ProductCharacteristic?.Trim() ?? string.Empty,
            ProductSeries = x.ProductSeries?.Trim() ?? string.Empty,
            ComponentArticle = x.ComponentArticle?.Trim() ?? string.Empty,
            ComponentCharacteristic = x.ComponentCharacteristic?.Trim() ?? string.Empty,
            ComponentSeries = x.ComponentSeries?.Trim() ?? string.Empty,
            Warehouse = x.Warehouse?.Trim() ?? string.Empty,
            QuantityPerUnit = x.QuantityPerUnit,
            IsMaterial = x.IsMaterial,
            IsSemiFinished = x.IsSemiFinished,
        }).ToList();

        _db.ProductCompositions.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Product compositions synced. Warehouse: {Warehouse}, Count: {Count}.", warehouse, entities.Count);
        return Ok(new SyncRawDataResponse(true, entities.Count, DateTimeOffset.UtcNow));
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
