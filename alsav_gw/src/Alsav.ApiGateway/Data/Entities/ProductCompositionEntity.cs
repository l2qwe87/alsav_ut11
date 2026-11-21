namespace Alsav.ApiGateway.Data.Entities;

public sealed class ProductCompositionEntity
{
    public long Id { get; set; }
    public string ProductArticle { get; set; } = string.Empty;
    public string ProductCharacteristic { get; set; } = string.Empty;
    public string ProductSeries { get; set; } = string.Empty;
    public string ComponentArticle { get; set; } = string.Empty;
    public string ComponentCharacteristic { get; set; } = string.Empty;
    public string ComponentSeries { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public decimal QuantityPerUnit { get; set; }
    public bool IsMaterial { get; set; }
    public bool IsSemiFinished { get; set; }
}
