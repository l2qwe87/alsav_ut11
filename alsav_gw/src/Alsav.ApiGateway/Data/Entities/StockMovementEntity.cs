namespace Alsav.ApiGateway.Data.Entities;

public sealed class StockMovementEntity
{
    public long Id { get; set; }
    public DateTimeOffset Period { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public string Article { get; set; } = string.Empty;
    public string Characteristic { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public decimal InStockDelta { get; set; }
    public decimal ReservedInWarehouseDelta { get; set; }
    public decimal ReservedForOrderDelta { get; set; }
    public decimal PaidUnsecuredDemandDelta { get; set; }
}
