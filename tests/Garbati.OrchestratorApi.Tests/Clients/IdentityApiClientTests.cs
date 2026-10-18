using System.Net;
using Garbati.OrchestratorApi.Clients.Contracts.Identity;
using Garbati.OrchestratorApi.Clients.Implementations;
using Garbati.OrchestratorApi.Exceptions;

namespace Garbati.OrchestratorApi.Tests.Clients;

// Exercises IdentityApiClient's own request-building and response-parsing against the exact response
// shapes read from Garbati.IdentityApi's live source (Controllers/CustomersController.cs,
// Controllers/AuthorizationController.cs, Middleware/ExceptionHandlingMiddleware.cs) - without a real
// network call. This is the judgment call's "mocked HTTP client" approach applied one layer lower than
// the service tests: it proves this client parses IdentityApi's actual response shapes correctly, which
// the service-level fakes (Tests/Fixtures) do not, since they never touch JSON at all.
public class IdentityApiClientTests
{
    private static IdentityApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://identity.invalid") });

    [Fact]
    public async Task CreateCustomerAsync_ParsesASuccessResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"customerId":"CUS-ABC123","customerReference":null,"customerStatus":"Active","kycStatus":"Pending","kycTier":null,"jurisdictionCode":"NG","createdAt":"2026-10-18T00:00:00Z","updatedAt":"2026-10-18T00:00:00Z"}""");
        var client = CreateClient(handler);

        var response = await client.CreateCustomerAsync(
            new CreateCustomerRequest("Ada Lovelace", "NG", null, null, null, "key-1"), CancellationToken.None);

        Assert.Equal("CUS-ABC123", response.CustomerId);
        Assert.Equal("Pending", response.KycStatus);
        Assert.Equal("POST", handler.LastRequest!.Method.Method);
        Assert.Equal("http://identity.invalid/v1/customers", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task RequestAuthorizationAsync_OnA422_ThrowsAuthorizationDeniedException_WithDecisionIdAndReasonCode()
    {
        // Shape confirmed against Garbati.IdentityApi.Middleware.ExceptionHandlingMiddleware: ProblemDetails
        // with decisionId/reasonCode flattened into the top level as extensions.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"title":"Authorization denied.","detail":"No active mandate.","correlationId":"c1","decisionId":"DEC-999","reasonCode":"NoActiveMandate"}""",
            contentType: "application/problem+json");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(() => client.RequestAuthorizationAsync(
            new AuthorizationRequest("CUS-1", "Transfer", "ACC-1", "ACC-2", 100m, "NGN", "key-2"), CancellationToken.None));

        Assert.Equal("DEC-999", ex.DecisionId);
        Assert.Equal("NoActiveMandate", ex.ReasonCode);
        Assert.Equal("No active mandate.", ex.Message);
    }

    [Fact]
    public async Task AttachMandateAsync_OnA409_ThrowsMandateAlreadyAttachedException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Conflict,
            """{"status":409,"title":"Duplicate active mandate.","detail":"Customer already has an active mandate on this account.","correlationId":"c1"}""",
            contentType: "application/problem+json");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<MandateAlreadyAttachedException>(() => client.AttachMandateAsync(
            "CUS-1", new AttachMandateRequest("1000000001", ["View", "Debit", "Credit"]), CancellationToken.None));
    }

    [Fact]
    public async Task CreateCustomerAsync_OnA400_ThrowsDownstreamApiException_WithTheValidationErrors()
    {
        // Confirmed against the live controller: BadRequest(errors) where errors is a plain
        // List<string> - a JSON array, NOT application/problem+json, unlike every other error status.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.BadRequest, """["FullName is required."]""");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => client.CreateCustomerAsync(
            new CreateCustomerRequest("", "NG", null, null, null, "key-3"), CancellationToken.None));

        Assert.Equal("IdentityApi", ex.ServiceName);
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("FullName is required.", ex.Message);
    }
}
