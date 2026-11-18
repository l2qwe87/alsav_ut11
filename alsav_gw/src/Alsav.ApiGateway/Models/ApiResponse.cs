using System.Text.Json.Serialization;

namespace Alsav.ApiGateway.Models;

/// <summary>
/// Единый формат ошибки API.
/// </summary>
/// <param name="Error">Краткое описание ошибки.</param>
/// <param name="Details">Дополнительные сведения (например, список ошибок валидации).</param>
public sealed record ErrorResponse(
    string Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Details = null);

/// <summary>
/// Ответ внутреннего endpoint синхронизации остатков.
/// </summary>
/// <param name="Success">Признак успешной обработки.</param>
/// <param name="Count">Количество принятых позиций.</param>
/// <param name="Timestamp">Время обновления кэша в UTC.</param>
public sealed record SyncBalancesResponse(bool Success, int Count, DateTimeOffset Timestamp);

/// <summary>
/// Ответ внешнего endpoint остатков.
/// </summary>
/// <param name="Data">Страница остатков.</param>
/// <param name="Total">Общее количество позиций после фильтрации.</param>
/// <param name="LastUpdate">Время последнего обновления кэша в UTC.</param>
public sealed record BalancesResponse(
    IReadOnlyList<BalanceItem> Data,
    int Total,
    DateTimeOffset LastUpdate);

/// <summary>
/// Ответ health-check.
/// </summary>
/// <param name="Status">Текущий статус сервиса.</param>
/// <param name="BalancesLastUpdate">Время последнего обновления кэша остатков.</param>
/// <param name="BalancesCount">Количество позиций в кэше остатков.</param>
/// <param name="ApiKeysLastSync">Время последней успешной синхронизации API-ключей.</param>
/// <param name="ApiKeysCount">Количество API-ключей в кэше.</param>
public sealed record HealthResponse(
    string Status,
    DateTimeOffset? BalancesLastUpdate,
    int BalancesCount,
    DateTimeOffset? ApiKeysLastSync,
    int ApiKeysCount);
