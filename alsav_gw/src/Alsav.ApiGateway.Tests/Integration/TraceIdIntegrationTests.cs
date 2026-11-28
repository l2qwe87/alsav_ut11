using System.Net;
using System.Text.Json;
using Alsav.ApiGateway.Middleware;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Alsav.ApiGateway.Tests.Integration;

public sealed class TraceIdIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TraceIdIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GatewayDb"] = $"Data Source={Path.Combine(Path.GetTempPath(), $"gw-trace-{Guid.NewGuid():N}.db")}",
                    ["InternalApi:SecretKey"] = "test-internal-secret",
                    ["StockSeed:Enabled"] = "false"
                });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Health_ReturnsTraceIdHeaders()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(GetHeader(response, TraceIdMiddleware.HeaderName)));
        Assert.Equal(
            GetHeader(response, TraceIdMiddleware.HeaderName),
            GetHeader(response, TraceIdMiddleware.AliasHeaderName));
    }

    [Fact]
    public async Task IncomingTraceId_IsReusedInResponse()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(TraceIdMiddleware.HeaderName, "integration-trace-007");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("integration-trace-007", GetHeader(response, TraceIdMiddleware.HeaderName));
        Assert.Equal("integration-trace-007", GetHeader(response, TraceIdMiddleware.AliasHeaderName));
    }

    [Fact]
    public async Task UnauthorizedResponse_ContainsTraceIdInHeaderAndBody()
    {
        var response = await _client.GetAsync("/clientApi/stocks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var headerTraceId = GetHeader(response, TraceIdMiddleware.HeaderName);
        Assert.False(string.IsNullOrWhiteSpace(headerTraceId));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(headerTraceId, body.RootElement.GetProperty("traceId").GetString());
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values), $"Missing {name} header.");
        return Assert.Single(values);
    }
}
