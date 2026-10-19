using System.Net;
using System.Text;
using System.Text.Json;
using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Middleware;
using Garbati.OrchestratorApi.Tests.Fixtures;

namespace Garbati.OrchestratorApi.Tests.Authorization;

// Task file: "API-key auth: missing/wrong scope on both endpoints." Same test shape as LedgerApi's/
// IdentityApi's AuthorizationTests.
[Collection("Api")]
public class AuthorizationTests(ApiFixture fixture)
{
    private static string StartAccountOpeningBody() => """
        {"idempotencyKey":"auth-test-start","fullName":"Auth Test","jurisdictionCode":"NG",
         "accountName":"Auth Test Savings","accountType":"Savings","currencyCode":"NGN"}
        """;
    private static string TransferBody() => """
        {"idempotencyKey":"auth-test-transfer","requestedBy":"CUS-DOESNOTEXIST","debitAccountId":"1000000001",
         "creditAccountId":"1000000002","amount":100,"currency":"NGN"}
        """;
    private static string ReversalBody() => """
        {"idempotencyKey":"auth-test-reversal","requestedBy":"CUS-DOESNOTEXIST","originalTransactionReference":"TXN-DOESNOTEXIST",
         "amount":100,"currency":"NGN","reasonCode":"CUST"}
        """;

    public static TheoryData<string, string, string?, string> Endpoints => new()
    {
        { "POST", "/v1/account-openings", StartAccountOpeningBody(), OrchestratorScopes.AccountOpenings },
        { "GET", "/v1/account-openings/OPN-DOESNOTEXIST", null, OrchestratorScopes.AccountOpenings },
        { "POST", "/v1/account-openings/OPN-DOESNOTEXIST/retry", null, OrchestratorScopes.AccountOpenings },
        { "POST", "/v1/transfers", TransferBody(), OrchestratorScopes.Transfers },
        { "POST", "/v1/reversals", ReversalBody(), OrchestratorScopes.Reversals },
    };

    private static HttpRequestMessage Request(string method, string path, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns401_WithProblemDetails_WhenNoApiKeyIsSent(string method, string path, string? body, string scope)
    {
        _ = scope;
        using var client = fixture.CreateClient(clientId: null);

        using var response = await client.SendAsync(Request(method, path, body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiKeyAuthenticationHandler.SchemeName, response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(correlationId, problem.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns403_WhenTheKeyLacksTheRequiredScope(string method, string path, string? body, string scope)
    {
        using var client = fixture.CreateClient(ApiFixture.AllButClient(scope));

        using var response = await client.SendAsync(Request(method, path, body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ReachesTheEndpoint_WhenTheKeyHoldsOnlyTheRequiredScope(string method, string path, string? body, string scope)
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(scope));

        using var response = await client.SendAsync(Request(method, path, body));

        // Past authorization the controller answers normally (200, 404 or 422 depending on the data) -
        // never 401/403.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns401_ForAnUnknownApiKey()
    {
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "idk_not_a_real_key");

        using var response = await client.GetAsync("/v1/account-openings/OPN-DOESNOTEXIST");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
