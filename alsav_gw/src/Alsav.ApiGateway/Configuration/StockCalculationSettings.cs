using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Configuration;

public sealed class StockCalculationSettings
{
    public const string SectionName = "StockCalculation";

    [Required]
    public string DefaultWarehouse { get; set; } = "Склад адресного хранения";
}
