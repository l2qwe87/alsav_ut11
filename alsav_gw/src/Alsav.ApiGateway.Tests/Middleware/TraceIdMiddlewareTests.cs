using Alsav.ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Alsav.ApiGateway.Tests.Middleware;

public sealed class TraceIdMiddlewareTests
{
    private static TraceIdMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new TraceIdMiddleware(next, NullLogger<TraceIdMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string? incomingTraceId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/health";
        context.Response.Body = new MemoryStream();

        if (incomingTraceId is not null)
        {
            context.Request.Headers[TraceIdMiddleware.HeaderName] = incomingTraceId;
        }

        return context;
    }

    [Fact]
    public async Task InvokeAsync_GeneratesTraceIdAndWritesHeaders()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        var traceId = TraceIdMiddleware.GetTraceId(context);
        Assert.True(nextCalled);
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(traceId, context.Response.Headers[TraceIdMiddleware.HeaderName].ToString());
        Assert.Equal(traceId, context.Response.Headers[TraceIdMiddleware.AliasHeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_UsesValidIncomingTraceId()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("client-trace-123");

        await middleware.InvokeAsync(context);

        Assert.Equal("client-trace-123", TraceIdMiddleware.GetTraceId(context));
        Assert.Equal("client-trace-123", context.Response.Headers[TraceIdMiddleware.HeaderName].ToString());
        Assert.Equal("client-trace-123", context.Response.Headers[TraceIdMiddleware.AliasHeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_IgnoresInvalidIncomingTraceId()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext(new string('x', 129));

        await middleware.InvokeAsync(context);

        var traceId = TraceIdMiddleware.GetTraceId(context);
        Assert.NotEqual(new string('x', 129), traceId);
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(traceId, context.Response.Headers[TraceIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WritesHeadersBeforeNextThrows()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));
        var context = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        var traceId = TraceIdMiddleware.GetTraceId(context);
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(traceId, context.Response.Headers[TraceIdMiddleware.HeaderName].ToString());
    }
}
