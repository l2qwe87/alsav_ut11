using System.Text;
using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Middleware;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Alsav.ApiGateway.Tests.Middleware;

public sealed class ApiKeyValidationMiddlewareTests
{
    private static ApiKeyCacheService CreateApiKeyCache(string validKey)
    {
        var mock = new Mock<ApiKeyCacheService>(
            Mock.Of<IHttpClientFactory>(),
            Options.Create(new OneCSettings
            {
                BaseUrl = "http://localhost",
                InternalSecret = "test-internal-secret",
                SyncIntervalMinutes = 10
            }),
            NullLogger<ApiKeyCacheService>.Instance);

        mock.Setup(cache => cache.IsValidKey(validKey)).Returns(true);
        return mock.Object;
    }

    private static (ApiKeyValidationMiddleware Middleware, DefaultHttpContext Context, Func<bool> NextCalled)
        CreateMiddleware(ApiKeyCacheService cache, string path, string? apiKey)
    {
        var nextCalled = false;
        var middleware = new ApiKeyValidationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            cache,
            NullLogger<ApiKeyValidationMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.Response.Body = new MemoryStream();

        if (apiKey is not null)
        {
            context.Request.Headers[ApiKeyValidationMiddleware.HeaderName] = apiKey;
        }

        return (middleware, context, () => nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WithoutApiKey_ReturnsUnauthorized()
    {
        var (middleware, context, nextCalled) = CreateMiddleware(
            CreateApiKeyCache("valid-key"),
            "/clientApi/balances",
            apiKey: null);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("Missing X-API-Key header", await ReadBodyAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidApiKey_ReturnsUnauthorized()
    {
        var (middleware, context, nextCalled) = CreateMiddleware(
            CreateApiKeyCache("valid-key"),
            "/clientApi/balances",
            apiKey: "invalid-key");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("Invalid or revoked API Key", await ReadBodyAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_WithValidApiKey_CallsNext()
    {
        var (middleware, context, nextCalled) = CreateMiddleware(
            CreateApiKeyCache("valid-key"),
            "/clientApi/balances",
            apiKey: "valid-key");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ForNonProtectedPath_CallsNextWithoutApiKey()
    {
        var (middleware, context, nextCalled) = CreateMiddleware(
            CreateApiKeyCache("valid-key"),
            "/health",
            apiKey: null);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_PathCheckIsCaseInsensitive()
    {
        var (middleware, context, nextCalled) = CreateMiddleware(
            CreateApiKeyCache("valid-key"),
            "/CLIENTAPI/balances",
            apiKey: null);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(
            context.Response.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
