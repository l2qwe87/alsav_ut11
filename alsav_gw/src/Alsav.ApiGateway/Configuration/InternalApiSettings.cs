using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Configuration;

/// <summary>
/// Настройки внутреннего API для push-синхронизации данных.
/// </summary>
public sealed class InternalApiSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "InternalApi";

    /// <summary>
    /// Секрет для заголовка <c>X-Internal-Secret</c>.
    /// </summary>
    [Required(ErrorMessage = "InternalApi:SecretKey is required.")]
    [MinLength(8, ErrorMessage = "InternalApi:SecretKey must contain at least 8 characters.")]
    public string SecretKey { get; set; } = string.Empty;
}
