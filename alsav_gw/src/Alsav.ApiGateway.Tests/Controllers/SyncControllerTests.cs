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
        StockCacheService cache,
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

    private static StockItem[] ValidItems()
    {
        return new[]
        {
            new StockItem
            {
                Article = "ART-1",
                Name = "Product",
                Unit = "pcs",
                Type = "Product",
                Price = 100,
                Stock = 5
            }
        };
    }

    [Fact]
    public void SyncStocks_WithValidSecretAndPayload_ReturnsOkAndUpdatesCache()
    {
        var cache = new StockCacheService();
        var controller = CreateController(cache);

        var result = controller.SyncStocks(ValidItems());

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SyncStocksResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(1, response.Count);
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void SyncStocks_WithMissingSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(new StockCacheService(), providedSecret: null);

        var result = controller.SyncStocks(ValidItems());

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(unauthorized.Value);
        Assert.Contains("X-Internal-Secret", error.Error);
    }

    [Fact]
    public void SyncStocks_WithInvalidSecret_ReturnsUnauthorized()
    {
        var controller = CreateController(new StockCacheService(), providedSecret: "wrong-secret");

        var result = controller.SyncStocks(ValidItems());

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void SyncStocks_WithEmptyPayload_ReturnsBadRequest()
    {
        var controller = CreateController(new StockCacheService());

        var result = controller.SyncStocks(Array.Empty<StockItem>());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<ErrorResponse>(badRequest.Value);
    }

    [Fact]
    public void SyncStocks_WithNullPayload_ReturnsBadRequest()
    {
        var controller = CreateController(new StockCacheService());

        var result = controller.SyncStocks(null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void SyncStocks_WithMissingRequiredFields_ReturnsBadRequest()
    {
        var controller = CreateController(new StockCacheService());
        var items = new[] { new StockItem { Article = "ART-1" } };

        var result = controller.SyncStocks(items);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Equal("Validation failed", error.Error);
        Assert.Contains("Name", error.Details ?? string.Empty);
    }
}
