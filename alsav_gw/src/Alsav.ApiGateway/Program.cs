using System.Reflection;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Middleware;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.OpenApi;
using Alsav.ApiGateway.Services;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Development.private.json", optional: true, reloadOnChange: true);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Alsav.ApiGateway"));

builder.Services.AddControllers();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = string.Join(
            "; ",
            context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors
                    .Select(error => $"{entry.Key}: {error.ErrorMessage}")));

        return new BadRequestObjectResult(new ErrorResponse("Validation failed", details));
    };
});

builder.Services
    .AddOptions<InternalApiSettings>()
    .Bind(builder.Configuration.GetSection(InternalApiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BalanceCalculationSettings>()
    .Bind(builder.Configuration.GetSection(BalanceCalculationSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("GatewayDb")
    ?? "Data Source=gateway.db";
builder.Services.AddDbContext<GatewayDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<BalanceCalculationService>();

builder.Services
    .AddOptions<BalanceSeedOptions>()
    .Bind(builder.Configuration.GetSection(BalanceSeedOptions.SectionName));

builder.Services.AddHttpClient(BalanceSeedService.HttpClientName);
builder.Services.AddSingleton<BalanceCacheService>();

builder.Services.AddSingleton<ApiKeyCacheService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ApiKeyCacheService>());
builder.Services.AddHostedService<BalanceSeedService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("external", new OpenApiInfo
    {
        Title = "ALSAV Client API",
        Version = "v1",
        Description = "External API for clients."
    });

    options.SwaggerDoc("internal", new OpenApiInfo
    {
        Title = "ALSAV Internal API",
        Version = "v1",
        Description = "Internal API for internal network."
    });

    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (string.Equals(docName, "internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(docName, "external", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(apiDesc.GroupName, "external", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    });

    options.DocumentFilter<SwaggerSecurityDocumentFilter>();
    options.OperationFilter<SwaggerSecurityOperationFilter>();

    var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFileName);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    var dbPath = db.Database.GetDbConnection().DataSource;
    if (!string.IsNullOrWhiteSpace(dbPath) && !string.Equals(dbPath, ":memory:", StringComparison.OrdinalIgnoreCase))
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS api_keys (
            "Key" TEXT NOT NULL CONSTRAINT "PK_api_keys" PRIMARY KEY,
            "ClientId" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_api_keys_ClientId" ON "api_keys" ("ClientId");""");
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_api_keys_Key" ON "api_keys" ("Key");""");
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ApiKeyValidationMiddleware>();

app.Use(async (context, next) =>
{
    const string clientSwagger = "/clientApi/swagger";
    if (context.Request.Path.StartsWithSegments(clientSwagger, StringComparison.OrdinalIgnoreCase))
    {
        var remainder = context.Request.Path.Value?[clientSwagger.Length..] ?? string.Empty;
        if (remainder.Equals("/internal/swagger.json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (remainder.Equals("/external/swagger.json", StringComparison.OrdinalIgnoreCase))
        {
            var originalPath = context.Request.Path;
            context.Request.Path = "/swagger/external/swagger.json";
            context.Items["ClientApiSwagger"] = true;
            try { await next(context); }
            finally { context.Request.Path = originalPath; }
            return;
        }
    }

    await next(context);
});

app.UseSwagger(c =>
{
    c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
    {
        if (httpReq.Path.StartsWithSegments("/clientApi/swagger", StringComparison.OrdinalIgnoreCase)
            || httpReq.HttpContext.Items.ContainsKey("ClientApiSwagger")
            || httpReq.Headers.Referer.ToString().Contains("/clientApi/swagger", StringComparison.OrdinalIgnoreCase))
        {
            swaggerDoc.Servers = [new OpenApiServer { Url = "/clientApi" }];
        }
    });
});

app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "clientApi/swagger";
    options.SwaggerEndpoint("external/swagger.json", "External");
});

app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("external/swagger.json", "External");
    options.SwaggerEndpoint("internal/swagger.json", "Internal");
});

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
    if (addresses is null || addresses.Count == 0)
    {
        app.Logger.LogInformation("Gateway started.");
        return;
    }

    foreach (var address in addresses)
    {
        app.Logger.LogInformation("Gateway listening on {Address}", address);
    }
});

app.Run();

/// <summary>
/// Точка входа приложения (используется интеграционными тестами).
/// </summary>
public partial class Program;
