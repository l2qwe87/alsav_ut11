using Alsav.ApiGateway.Middleware;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Alsav.ApiGateway.OpenApi;

/// <summary>
/// Документирует trace id во всех операциях Swagger.
/// </summary>
public sealed class SwaggerTraceIdOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = TraceIdMiddleware.HeaderName,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Необязательный trace id клиента. Если передан, gateway использует его и возвращает в ответе.",
            Schema = new OpenApiSchema { Type = "string" }
        });

        foreach (var response in operation.Responses.Values)
        {
            response.Headers ??= new Dictionary<string, OpenApiHeader>();
            response.Headers[TraceIdMiddleware.HeaderName] = new OpenApiHeader
            {
                Description = "Trace id запроса, используется для поиска логов.",
                Schema = new OpenApiSchema { Type = "string" }
            };
            response.Headers[TraceIdMiddleware.AliasHeaderName] = new OpenApiHeader
            {
                Description = "Псевдоним заголовка X-Trace-Id.",
                Schema = new OpenApiSchema { Type = "string" }
            };
        }
    }
}
