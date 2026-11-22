using System.Reflection;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Middleware;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
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
    .AddOptions<OneCSettings>()
    .Bind(builder.Configuration.GetSection(OneCSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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

builder.Services.AddSingleton<BalanceCacheService>();

builder.Services.AddSingleton<ApiKeyCacheService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ApiKeyCacheService>());

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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
    var originalPath = context.Request.Path;
    const string clientSwagger = "/clientApi/swagger";
    if (originalPath.StartsWithSegments(clientSwagger, StringComparison.OrdinalIgnoreCase))
    {
        var remainder = originalPath.Value?[clientSwagger.Length..] ?? string.Empty;
        var swaggerPath = remainder.Length == 0 || remainder == "/"
            ? "/swagger/index.html"
            : "/swagger" + remainder;
        context.Request.Path = swaggerPath;
        try
        {
            await next(context);
        }
        finally
        {
            context.Request.Path = originalPath;
        }
        return;
    }

    await next(context);
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/swagger/external/swagger.json", "External");
    options.SwaggerEndpoint("/swagger/internal/swagger.json", "Internal");
});

app.MapControllers();
app.MapReverseProxy();

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
