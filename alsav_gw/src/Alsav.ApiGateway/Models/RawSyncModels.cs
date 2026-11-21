using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Models;

public sealed class StockSnapshotItem
{
    [Required] public DateTimeOffset SnapshotDate { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    [Required] public string Article { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    [Required] public string Name { get; set; } = string.Empty;
    public string Characteristic { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal InStock { get; set; }
    public decimal ReservedInWarehouse { get; set; }
    public decimal ReservedForOrder { get; set; }
    public decimal PaidUnsecuredDemand { get; set; }
}

public sealed class StockMovementItem
{
    [Required] public DateTimeOffset Period { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    [Required] public string Article { get; set; } = string.Empty;
    public string Characteristic { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public decimal InStockDelta { get; set; }
    public decimal ReservedInWarehouseDelta { get; set; }
    public decimal ReservedForOrderDelta { get; set; }
    public decimal PaidUnsecuredDemandDelta { get; set; }
}

public sealed class ProductCompositionItem
{
    [Required] public string ProductArticle { get; set; } = string.Empty;
    public string ProductCharacteristic { get; set; } = string.Empty;
    public string ProductSeries { get; set; } = string.Empty;
    [Required] public string ComponentArticle { get; set; } = string.Empty;
    public string ComponentCharacteristic { get; set; } = string.Empty;
    public string ComponentSeries { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.000001", "79228162514264337593543950335")]
    public decimal QuantityPerUnit { get; set; }
    public bool IsMaterial { get; set; }
    public bool IsSemiFinished { get; set; }
}

public sealed record SyncRawDataResponse(bool Success, int Count, DateTimeOffset Timestamp);
