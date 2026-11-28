using Alsav.ApiGateway.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Alsav.ApiGateway.Middleware;

/// <summary>
/// Добавляет trace id в тело ошибочного ответа контроллеров.
/// </summary>
public sealed class TraceIdResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ErrorResponse error })
        {
            error.TraceId = TraceIdMiddleware.GetTraceId(context.HttpContext);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
