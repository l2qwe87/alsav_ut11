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

builder.Services.AddControllers(options => options.Filters.Add<TraceIdResultFilter>());

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

        return new BadRequestObjectResult(new ErrorResponse("Validation failed", details)
        {
            TraceId = TraceIdMiddleware.GetTraceId(context.HttpContext)
        });
    };
});

builder.Services
    .AddOptions<InternalApiSettings>()
    .Bind(builder.Configuration.GetSection(InternalApiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<StockCalculationSettings>()
    .Bind(builder.Configuration.GetSection(StockCalculationSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("GatewayDb")
    ?? "Data Source=gateway.db";
builder.Services.AddDbContext<GatewayDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<StockCalculationService>();

builder.Services
    .AddOptions<StockSeedOptions>()
    .Bind(builder.Configuration.GetSection(StockSeedOptions.SectionName));

builder.Services.AddHttpClient(StockSeedService.HttpClientName);
builder.Services.AddSingleton<StockCacheService>();

builder.Services.AddSingleton<ApiKeyCacheService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ApiKeyCacheService>());
builder.Services.AddHostedService<StockSeedService>();

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
    options.OperationFilter<SwaggerTraceIdOperationFilter>();

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
            "Id" INTEGER NOT NULL CONSTRAINT "PK_api_keys" PRIMARY KEY AUTOINCREMENT,
            "Key" TEXT NOT NULL,
            "ClientId" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL
        );
        """);
    var apiKeyColumns = db.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('api_keys')").ToList();
    if (!apiKeyColumns.Contains("Id", StringComparer.OrdinalIgnoreCase))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE api_keys RENAME TO api_keys_old;");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE api_keys (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_api_keys" PRIMARY KEY AUTOINCREMENT,
                "Key" TEXT NOT NULL,
                "ClientId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""INSERT INTO api_keys ("Key","ClientId","CreatedAt") SELECT "Key","ClientId","CreatedAt" FROM api_keys_old;""");
        db.Database.ExecuteSqlRaw("DROP TABLE api_keys_old;");
    }
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_api_keys_ClientId" ON "api_keys" ("ClientId");""");
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_api_keys_Key" ON "api_keys" ("Key");""");
}

app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var traceId = TraceIdMiddleware.GetTraceId(httpContext);
        diagnosticContext.Set(TraceIdMiddleware.LogProperty, traceId);
        diagnosticContext.Set("trace_id", traceId);
    };
});
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

app.UseSwagger();

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
