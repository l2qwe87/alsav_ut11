using Microsoft.EntityFrameworkCore;

namespace Alsav.ApiGateway.Data.Entities;

public sealed class StockSnapshotEntity
{
    public long Id { get; set; }
    public DateTimeOffset SnapshotDate { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Article { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Characteristic { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal InStock { get; set; }
    public decimal ReservedInWarehouse { get; set; }
    public decimal ReservedForOrder { get; set; }
    public decimal PaidUnsecuredDemand { get; set; }
}
