using System.Net;
using Garbati.OrchestratorApi.Clients.Contracts.Ledger;
using Garbati.OrchestratorApi.Clients.Implementations;
using Garbati.OrchestratorApi.Exceptions;

namespace Garbati.OrchestratorApi.Tests.Clients;

// See IdentityApiClientTests' comment: same approach, against LedgerApi's actual response shapes
// (Controllers/AccountsController.cs, Controllers/TransfersController.cs).
public class LedgerApiClientTests
{
    private static LedgerApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ledger.invalid") });

    [Fact]
    public async Task CreateAccountAsync_ParsesASuccessResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"accountNumber":"1234567890","accountName":"Ada Savings","accountType":"Savings","accountClass":"Customer","currencyCode":"NGN","status":"Active","createdAt":"2026-10-18T00:00:00Z"}""");
        var client = CreateClient(handler);

        var response = await client.CreateAccountAsync(
            new CreateAccountRequest("Ada Savings", "Savings", "NGN", "key-1"), CancellationToken.None);

        Assert.Equal("1234567890", response.AccountNumber);
        Assert.Equal("api/accounts", handler.LastRequest!.RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task TransferAsync_OnA422_ReturnsTheFailedTransferResponse_RatherThanThrowing()
    {
        // Confirmed against the live controller: a business-rule failure (TransactionStatus.Failed) is
        // committed and returned with 422, but the body is still an ordinary TransferResponse - data for
        // TransferService to act on, not an error to wrap.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.UnprocessableEntity,
            """{"reference":"TXN-FAILED1","status":"Failed","failureReason":"Insufficient funds."}""");
        var client = CreateClient(handler);

        var response = await client.TransferAsync(
            new TransferRequest("1000000001", "1000000002", 500m, null, "key-2"), CancellationToken.None);

        Assert.Equal("Failed", response.Status);
        Assert.Equal("Insufficient funds.", response.FailureReason);
    }

    [Fact]
    public async Task TransferAsync_OnA404_ThrowsDownstreamApiException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound,
            """{"status":404,"title":"Account not found.","correlationId":"c1"}""",
            contentType: "application/problem+json");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => client.TransferAsync(
            new TransferRequest("1000000001", "1000000002", 500m, null, "key-3"), CancellationToken.None));

        Assert.Equal("LedgerApi", ex.ServiceName);
        Assert.Equal(404, ex.StatusCode);
    }
}
