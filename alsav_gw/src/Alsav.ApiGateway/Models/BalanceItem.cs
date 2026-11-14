using System.Text.Json.Serialization;

namespace Alsav.ApiGateway.Models;

/// <summary>
/// Позиция актуального остатка номенклатуры, получаемая из 1С.
/// </summary>
public sealed class BalanceItem
{
    /// <summary>
    /// Серия товара (из дополнительного реквизита «Серия»).
    /// </summary>
    public string Series { get; set; } = string.Empty;

    /// <summary>
    /// Штрихкод продукта.
    /// </summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// Артикул номенклатуры.
    /// </summary>
    public string Article { get; set; } = string.Empty;

    /// <summary>
    /// Единица измерения.
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Наименование номенклатуры.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Характеристика номенклатуры.
    /// </summary>
    public string Characteristic { get; set; } = string.Empty;

    /// <summary>
    /// Отпускная цена.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Вид номенклатуры.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Остаток, доступный для сборки.
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// Композитный ключ кэша: «Артикул|Характеристика|Серия».
    /// </summary>
    [JsonIgnore]
    public string CompositeKey => $"{Article}|{Characteristic}|{Series}".Trim('|');
}
