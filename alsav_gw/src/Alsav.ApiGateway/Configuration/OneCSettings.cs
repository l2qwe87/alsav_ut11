using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Configuration;

/// <summary>
/// Настройки интеграции с 1С.
/// </summary>
public sealed class OneCSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "OneCSettings";

    /// <summary>
    /// Базовый адрес HTTP-сервиса 1С, например <c>http://onec-http:80</c>.
    /// </summary>
    [Required(ErrorMessage = "OneCSettings:BaseUrl is required.")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Секрет для доступа к внутренним HTTP-сервисам 1С.
    /// </summary>
    [Required(ErrorMessage = "OneCSettings:InternalSecret is required.")]
    [MinLength(8, ErrorMessage = "OneCSettings:InternalSecret must contain at least 8 characters.")]
    public string InternalSecret { get; set; } = string.Empty;

    /// <summary>
    /// Период синхронизации API-ключей в минутах.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "OneCSettings:SyncIntervalMinutes must be between 1 and 1440.")]
    public int SyncIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Путь HTTP-сервиса 1С, возвращающего список API-ключей.
    /// </summary>
    public string KeysEndpointPath { get; set; } = "/hs/ClientAPI/keys";
}
