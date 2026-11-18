using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;

namespace Alsav.ApiGateway.Middleware;

/// <summary>
/// Проверяет API-ключ для всех запросов, начинающихся с <c>/clientApi</c>.
/// </summary>
public sealed class ApiKeyValidationMiddleware
{
    /// <summary>
    /// Имя заголовка с API-ключом.
    /// </summary>
    public const string HeaderName = "X-API-Key";

    private const string ProtectedPath = "/clientApi";

    private readonly RequestDelegate _next;
    private readonly ApiKeyCacheService _apiKeyCache;
    private readonly ILogger<ApiKeyValidationMiddleware> _logger;

    /// <summary>
    /// Создаёт экземпляр middleware.
    /// </summary>
    /// <param name="next">Следующий делегат конвейера.</param>
    /// <param name="apiKeyCache">Кэш действующих API-ключей.</param>
    /// <param name="logger">Логгер.</param>
    public ApiKeyValidationMiddleware(
        RequestDelegate next,
        ApiKeyCacheService apiKeyCache,
        ILogger<ApiKeyValidationMiddleware> logger)
    {
        _next = next;
        _apiKeyCache = apiKeyCache;
        _logger = logger;
    }

    /// <summary>
    /// Обрабатывает HTTP-запрос.
    /// </summary>
    /// <param name="context">Контекст HTTP-запроса.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(ProtectedPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var headerValues) || headerValues.Count == 0)
        {
            _logger.LogWarning(
                "Rejected request without API key. Method: {Method}, Path: {Path}, RemoteIp: {RemoteIp}.",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            await WriteUnauthorizedAsync(context, "Missing X-API-Key header");
            return;
        }

        var apiKey = headerValues[0];
        if (string.IsNullOrWhiteSpace(apiKey) || !_apiKeyCache.IsValidKey(apiKey))
        {
            _logger.LogWarning(
                "Rejected request with invalid API key. Method: {Method}, Path: {Path}, RemoteIp: {RemoteIp}.",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            await WriteUnauthorizedAsync(context, "Invalid or revoked API Key");
            return;
        }

        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string error)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(
            new ErrorResponse(error),
            context.RequestAborted);
    }
}
