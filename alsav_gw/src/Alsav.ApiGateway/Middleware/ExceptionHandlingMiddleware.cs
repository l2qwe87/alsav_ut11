using Alsav.ApiGateway.Models;

namespace Alsav.ApiGateway.Middleware;

/// <summary>
/// Глобальный обработчик необработанных исключений.
/// </summary>
/// <remarks>
/// Возвращает клиенту единообразный JSON-ответ и логирует ошибку.
/// </remarks>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Создаёт экземпляр middleware.
    /// </summary>
    /// <param name="next">Следующий делегат конвейера.</param>
    /// <param name="logger">Логгер.</param>
    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Обрабатывает HTTP-запрос.
    /// </summary>
    /// <param name="context">Контекст HTTP-запроса.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Клиент отключился — ответ писать некуда.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception while processing {Method} {Path}. TraceId: {TraceId}.",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response has already started; rethrowing exception.");
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(
                new ErrorResponse("Internal server error", context.TraceIdentifier),
                context.RequestAborted);
        }
    }
}
