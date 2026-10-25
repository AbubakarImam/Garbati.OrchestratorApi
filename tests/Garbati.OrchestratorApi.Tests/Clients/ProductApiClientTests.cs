using System.Net;
using Garbati.OrchestratorApi.Clients.Contracts.Product;
using Garbati.OrchestratorApi.Clients.Implementations;
using Garbati.OrchestratorApi.Exceptions;

namespace Garbati.OrchestratorApi.Tests.Clients;

// See IdentityApiClientTests' comment: same approach, against Garbati.ProductApi's actual response shapes
// (Controllers/AccountBindingsController.cs).
public class ProductApiClientTests
{
    private static ProductApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://product.invalid") });

    [Fact]
    public async Task BindAccountToProductAsync_ParsesASuccessResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"accountNumber":"1000000001","productCode":"NG-SAV-STD","boundAt":"2026-10-25T00:00:00Z"}""");
        var client = CreateClient(handler);

        var response = await client.BindAccountToProductAsync(
            new BindAccountToProductRequest("key-1", "1000000001", "NG-SAV-STD"), CancellationToken.None);

        Assert.Equal("1000000001", response.AccountNumber);
        Assert.Equal("NG-SAV-STD", response.ProductCode);
        Assert.Equal("v1/account-bindings", handler.LastRequest!.RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task BindAccountToProductAsync_OnA404_ThrowsDownstreamApiException()
    {
        // Confirmed against the live AccountProductBindingService: ProductNotFoundException (the
        // ProductCode doesn't exist) always throws before any response body exists - no dual
        // "200 or non-2xx-with-body" case to special-case here, unlike LedgerApi's transfer call.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound,
            """{"status":404,"title":"Product 'NG-SAV-STD' was not found.","correlationId":"c1"}""",
            contentType: "application/problem+json");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => client.BindAccountToProductAsync(
            new BindAccountToProductRequest("key-2", "1000000001", "NG-SAV-STD"), CancellationToken.None));

        Assert.Equal("ProductApi", ex.ServiceName);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task BindAccountToProductAsync_OnA409_ThrowsDownstreamApiException()
    {
        // AccountAlreadyBoundException (the account is already bound to a different product) - also a
        // plain throw, confirmed against the live service.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Conflict,
            """{"status":409,"title":"Account '1000000001' is already bound to a product.","correlationId":"c2"}""",
            contentType: "application/problem+json");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => client.BindAccountToProductAsync(
            new BindAccountToProductRequest("key-3", "1000000001", "NG-SAV-STD"), CancellationToken.None));

        Assert.Equal("ProductApi", ex.ServiceName);
        Assert.Equal(409, ex.StatusCode);
    }
}
