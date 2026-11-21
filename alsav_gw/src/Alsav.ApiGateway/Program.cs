using System.Reflection;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Data;
using Alsav.ApiGateway.Middleware;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpClient(ApiKeyCacheService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

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
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ApiKeyValidationMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "clientApi/swagger";
    options.SwaggerEndpoint("/swagger/external/swagger.json", "External");
});
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/swagger/external/swagger.json", "External");
    options.SwaggerEndpoint("/swagger/internal/swagger.json", "Internal");
});

app.MapControllers();
app.MapReverseProxy();

app.Run();

/// <summary>
/// Точка входа приложения (используется интеграционными тестами).
/// </summary>
public partial class Program;
