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
    public async Task SwaggerEndpoints_AreAvailable(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }
}
