using Alsav.ApiGateway.Configuration;
using Alsav.ApiGateway.Controllers.Internal;
using Alsav.ApiGateway.Models;
using Alsav.ApiGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Tests.Controllers;

public sealed class SyncControllerTests
{
    private const string Secret = "test-internal-secret";

    private static SyncController CreateController(
        BalanceCacheService cache,
        string? providedSecret = Secret)
    {
        var controller = new SyncController(
            cache,
            Options.Create(new InternalApiSettings { SecretKey = Secret }),
            NullLogger<SyncController>.Instance);

        var httpContext = new DefaultHttpContext();
        if (providedSecret is not null)
        {
            httpContext.Request.Headers[SyncController.SecretHeaderName] = providedSecret;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static BalanceItem[] ValidItems()
    {
        return new[]
        {
            new BalanceItem
            {
                Article = "ART-1",
                Name = "Product",
                Unit = "pcs",
                Type = "Product",
                Price = 100,
                Balance = 5
            }
        };
    }

    [Fact]
    public void SyncBalances_WithValidSecretAndPayload_ReturnsOkAndUpdatesCache()
    {
        var cache = new BalanceCacheService();
        var controller = CreateController(cache);

        var result = controller.SyncBalances(ValidItems());

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SyncBalancesResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(1, response.Count);
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void SyncBalances_WithMissingSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(new BalanceCacheService(), providedSecret: null);

        var result = controller.SyncBalances(ValidItems());

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(unauthorized.Value);
        Assert.Contains("X-Internal-Secret", error.Error);
    }

    [Fact]
    public void SyncBalances_WithInvalidSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(new BalanceCacheService(), providedSecret: "wrong-secret");

        var result = controller.SyncBalances(ValidItems());

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void SyncBalances_WithEmptyPayload_ReturnsBadRequest()
    {
        var controller = CreateController(new BalanceCacheService());

        var result = controller.SyncBalances(Array.Empty<BalanceItem>());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<ErrorResponse>(badRequest.Value);
    }

    [Fact]
    public void SyncBalances_WithNullPayload_ReturnsBadRequest()
    {
        var controller = CreateController(new BalanceCacheService());

        var result = controller.SyncBalances(null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void SyncBalances_WithMissingRequiredFields_ReturnsBadRequest()
    {
        var controller = CreateController(new BalanceCacheService());
        var items = new[] { new BalanceItem { Article = "ART-1" } };

        var result = controller.SyncBalances(items);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Equal("Validation failed", error.Error);
        Assert.Contains("Name", error.Details ?? string.Empty);
    }
}
