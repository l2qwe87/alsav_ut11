using Serilog.Context;

namespace Alsav.ApiGateway.Middleware;

/// <summary>
/// Формирует trace id для каждого HTTP-запроса и связывает его с логами и ответами.
/// </summary>
/// <remarks>
/// Если клиент передал корректный trace id, он используется повторно. Иначе создаётся новый.
/// </remarks>
public sealed class TraceIdMiddleware
{
    public const string HeaderName = "X-Trace-Id";
    public const string AliasHeaderName = "trace_id";
    public const string LogProperty = "TraceId";
    public const string ItemsKey = "Alsav.TraceId";

    private const int MaxTraceIdLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<TraceIdMiddleware> _logger;

    public TraceIdMiddleware(RequestDelegate next, ILogger<TraceIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = ResolveTraceId(context);
        context.Items[ItemsKey] = traceId;
        SetTraceIdHeaders(context, traceId);

        using (LogContext.PushProperty(LogProperty, traceId))
        using (LogContext.PushProperty("trace_id", traceId))
        {
            await _next(context);
        }
    }

    public static string GetTraceId(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemsKey, out var value)
            && value is string traceId
            && !string.IsNullOrWhiteSpace(traceId))
        {
            return traceId;
        }

        return context.TraceIdentifier;
    }

    public static void SetTraceIdHeaders(HttpContext context, string traceId)
    {
        context.Response.Headers[HeaderName] = traceId;
        context.Response.Headers[AliasHeaderName] = traceId;
    }

    public static void SetTraceIdHeaders(HttpContext context)
    {
        SetTraceIdHeaders(context, GetTraceId(context));
    }

    private string ResolveTraceId(HttpContext context)
    {
        foreach (var headerName in new[] { HeaderName, AliasHeaderName })
        {
            if (!context.Request.Headers.TryGetValue(headerName, out var values))
            {
                continue;
            }

            var candidate = values.FirstOrDefault()?.Trim();
            if (IsValidTraceId(candidate))
            {
                return candidate!;
            }

            _logger.LogWarning(
                "Ignoring invalid incoming {HeaderName} value. A new trace id will be generated.",
                headerName);
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValidTraceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTraceIdLength)
        {
            return false;
        }

        return value.All(character =>
            char.IsLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':');
    }
}
