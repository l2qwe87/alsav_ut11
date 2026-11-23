using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Alsav.ApiGateway.OpenApi;

public sealed class SwaggerSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var group = context.ApiDescription.GroupName;
        if (string.Equals(group, "external", StringComparison.OrdinalIgnoreCase))
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
                }] = Array.Empty<string>()
            });
            return;
        }

        if (string.Equals(group, "internal", StringComparison.OrdinalIgnoreCase))
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "InternalSecret" }
                }] = Array.Empty<string>()
            });
        }
    }
}
