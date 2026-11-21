using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Configuration;

public sealed class BalanceCalculationSettings
{
    public const string SectionName = "BalanceCalculation";

    [Required]
    public string DefaultWarehouse { get; set; } = "Склад адресного хранения";
}
