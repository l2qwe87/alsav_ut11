using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Alsav.ApiGateway.Middleware;

namespace Alsav.ApiGateway.OpenApi;

public sealed class SwaggerSecurityDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        swaggerDoc.Components.SecuritySchemes.Remove("ApiKey");
        swaggerDoc.Components.SecuritySchemes.Remove("InternalSecret");

        if (string.Equals(context.DocumentName, "external", StringComparison.OrdinalIgnoreCase))
        {
            swaggerDoc.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyValidationMiddleware.HeaderName,
                Description = "Ключ клиента для /clientApi/*"
            };
            return;
        }

        if (string.Equals(context.DocumentName, "internal", StringComparison.OrdinalIgnoreCase))
        {
            swaggerDoc.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyValidationMiddleware.HeaderName,
                Description = "Ключ клиента для /clientApi/*"
            };
            swaggerDoc.Components.SecuritySchemes["InternalSecret"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Internal-Secret",
                Description = "Секрет для /api/internal/v1/*"
            };
        }
    }
}
