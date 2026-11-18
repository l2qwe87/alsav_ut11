using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Configuration;

/// <summary>
/// Настройки внутреннего API, используемого регламентными заданиями 1С.
/// </summary>
public sealed class InternalApiSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "InternalApi";

    /// <summary>
    /// Секрет, который 1С передаёт в заголовке <c>X-Internal-Secret</c>.
    /// </summary>
    [Required(ErrorMessage = "InternalApi:SecretKey is required.")]
    [MinLength(8, ErrorMessage = "InternalApi:SecretKey must contain at least 8 characters.")]
    public string SecretKey { get; set; } = string.Empty;
}
