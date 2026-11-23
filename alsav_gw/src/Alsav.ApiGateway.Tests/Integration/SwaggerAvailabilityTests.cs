using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Alsav.ApiGateway.Tests.Integration;

public sealed class SwaggerAvailabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SwaggerAvailabilityTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GatewayDb"] = $"Data Source={Path.Combine(Path.GetTempPath(), $"gw-swagger-{Guid.NewGuid():N}.db")}",
                    ["InternalApi:SecretKey"] = "test-internal-secret"
                });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/clientApi/swagger/index.html")]
    [InlineData("/swagger/external/swagger.json")]
    [InlineData("/swagger/internal/swagger.json")]
    [InlineData("/clientApi/swagger/external/swagger.json")]
    public async Task SwaggerEndpoints_AreAvailable(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task ClientApiSwaggerConfig_UsesRelativeEndpoints()
    {
        var response = await _client.GetAsync("/clientApi/swagger/index.js");
        var js = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("external/swagger.json", js, StringComparison.Ordinal);
        Assert.DoesNotContain("internal/swagger.json", js, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"/swagger/", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientApiSwaggerInternalDoc_IsNotFound()
    {
        var response = await _client.GetAsync("/clientApi/swagger/internal/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClientApiSwaggerJson_SetsClientApiServer()
    {
        var response = await _client.GetAsync("/clientApi/swagger/external/swagger.json");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"/clientApi\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
