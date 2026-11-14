using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Middleware;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;
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

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ApiKeyValidationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapReverseProxy();

app.Run();

/// <summary>
/// Точка входа приложения (используется интеграционными тестами).
/// </summary>
public partial class Program;
