using System.Net;
using System.Text;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Alsav.ApiGateway.Tests.Services;

public sealed class ApiKeyCacheServiceTests
{
    private static ApiKeyCacheService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(ApiKeyCacheService.HttpClientName))
            .Returns(client);

        var options = Options.Create(new OneCSettings
        {
            BaseUrl = "http://onec-http:80",
            InternalSecret = "test-internal-secret",
            SyncIntervalMinutes = 10,
            KeysEndpointPath = "/hs/ClientAPI/keys"
        });

        return new ApiKeyCacheService(
            factory.Object,
            options,
            NullLogger<ApiKeyCacheService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public void IsValidKey_ReturnsFalse_WhenCacheIsEmpty()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse("[]")));
        var service = CreateService(handler);

        Assert.False(service.IsValidKey("any-key"));
        Assert.False(service.IsValidKey(null));
        Assert.False(service.IsValidKey(string.Empty));
        Assert.False(service.IsValidKey("   "));
    }

    [Fact]
    public async Task RefreshKeysAsync_LoadsKeysAndValidatesThem()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse("[\"key-1\",\"key-2\"]")));
        var service = CreateService(handler);

        var result = await service.RefreshKeysAsync();

        Assert.True(result);
        Assert.True(service.IsValidKey("key-1"));
        Assert.True(service.IsValidKey("key-2"));
        Assert.False(service.IsValidKey("key-3"));
        Assert.Equal(2, service.KeyCount);
        Assert.NotNull(service.GetLastSuccessfulSync());
    }

    [Fact]
    public async Task RefreshKeysAsync_SendsInternalSecretHeader()
    {
        string? capturedSecret = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedSecret = request.Headers.TryGetValues("X-Internal-Secret", out var values)
                ? values.Single()
                : null;

            return Task.FromResult(JsonResponse("[\"key-1\"]"));
        });
        var service = CreateService(handler);

        await service.RefreshKeysAsync();

        Assert.Equal("test-internal-secret", capturedSecret);
    }

    [Fact]
    public async Task RefreshKeysAsync_ReturnsFalse_OnHttpError_AndKeepsPreviousKeys()
    {
        var callCount = 0;

        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            callCount++;
            return Task.FromResult(callCount == 1
                ? JsonResponse("[\"key-1\"]")
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        var service = CreateService(handler);

        Assert.True(await service.RefreshKeysAsync());
        Assert.True(service.IsValidKey("key-1"));

        Assert.False(await service.RefreshKeysAsync());
        Assert.True(service.IsValidKey("key-1"));
    }

    [Fact]
    public async Task RefreshKeysAsync_ReturnsFalse_OnMalformedJson_AndKeepsPreviousKeys()
    {
        var callCount = 0;

        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            callCount++;
            return Task.FromResult(callCount == 1
                ? JsonResponse("[\"key-1\"]")
                : JsonResponse("not-a-json-array"));
        });
        var service = CreateService(handler);

        Assert.True(await service.RefreshKeysAsync());
        Assert.False(await service.RefreshKeysAsync());
        Assert.True(service.IsValidKey("key-1"));
    }

    [Fact]
    public async Task RefreshKeysAsync_ReturnsFalse_OnNetworkException()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("network error"));
        var service = CreateService(handler);

        var result = await service.RefreshKeysAsync();

        Assert.False(result);
        Assert.Equal(0, service.KeyCount);
        Assert.Null(service.GetLastSuccessfulSync());
    }

    [Fact]
    public async Task RefreshKeysAsync_FiltersEmptyKeys()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse("[\"key-1\",\"\",\"  \"]")));
        var service = CreateService(handler);

        Assert.True(await service.RefreshKeysAsync());
        Assert.Equal(1, service.KeyCount);
        Assert.True(service.IsValidKey("key-1"));
    }
}
