using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Exceptions;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Tests.Fixtures;

namespace Garbati.OrchestratorApi.Tests.Services;

// Task file's test list for transfer: a denial means LedgerApi is never called (call-count assertion);
// an approval calls LedgerApi and returns its result plus the decision id, never the grant string.
[Collection("Postgres")]
public class TransferServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static TransferService CreateService(
        OrchestratorDbContext ctx, FakeIdentityApiClient identity, FakeLedgerApiClient ledger) =>
        new(ctx, TestAudit.For(ctx), identity, ledger);

    private static TransferRequest NewRequest() => new(
        Guid.NewGuid().ToString(), RequestedBy: "CUS-TEST",
        DebitAccountId: "1000000001", CreditAccountId: "1000000002",
        Amount: 500m, Currency: "NGN", Narration: "test transfer");

    [Fact]
    public async Task TransferAsync_WhenIdentityApiDenies_DoesNotCallLedgerApi()
    {
        var identity = new FakeIdentityApiClient
        {
            RequestAuthorizationHandler = _ => throw new AuthorizationDeniedException(
                "DEC-DENY-1", "NoActiveMandate", "No active mandate permits this operation."),
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(() => service.TransferAsync(NewRequest()));

        Assert.Equal("DEC-DENY-1", ex.DecisionId);
        Assert.Equal("NoActiveMandate", ex.ReasonCode);
        Assert.Equal(1, identity.RequestAuthorizationCallCount);
        Assert.Equal(0, ledger.TransferCallCount);
    }

    [Fact]
    public async Task TransferAsync_WhenApproved_CallsLedgerApi_AndReturnsResultPlusDecisionId()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var service = CreateService(ctx, identity, ledger);

        var result = await service.TransferAsync(NewRequest());

        Assert.Equal(1, identity.RequestAuthorizationCallCount);
        Assert.Equal(1, ledger.TransferCallCount);
        Assert.StartsWith("DEC-FAKE", result.DecisionId);
        Assert.StartsWith("TXN-FAKE", result.Reference);
        Assert.Equal("Success", result.Status);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void TransferResultResponse_HasNoGrantField()
    {
        // DESIGN.md 1.5/1.12: "never the grant token itself." This is primarily a compile-time guarantee
        // (TransferResultResponse simply has no such property to set), but asserting it by reflection
        // keeps the rule visible to anyone editing this type later.
        var propertyNames = typeof(TransferResultResponse).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(propertyNames, name => name.Contains("Grant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TransferAsync_SendsDebitAsTheOperationToIdentityApi()
    {
        // T-024 regression test: the existing tests above mock IIdentityApiClient but never inspected the
        // Operation string actually sent in the AuthorizationRequest - that gap is exactly how a literal
        // "Transfer" (invalid; IdentityApi's AuthorizationRequestValidator only accepts MandateOperation
        // values View/Debit/Credit, or the literal "Reversal") shipped and made every live transfer call
        // fail with a 400 from IdentityApi, surfaced as an opaque 500 by this service. Capture the actual
        // request via the fake's handler and assert on its Operation field, not just that it was called.
        Garbati.OrchestratorApi.Clients.Contracts.Identity.AuthorizationRequest? capturedRequest = null;
        var identity = new FakeIdentityApiClient
        {
            RequestAuthorizationHandler = r =>
            {
                capturedRequest = r;
                return new Garbati.OrchestratorApi.Clients.Contracts.Identity.AuthorizationResponse(
                    new Garbati.OrchestratorApi.Clients.Contracts.Identity.AuthorizationDecisionResponse(
                        "DEC-FAKE0000000001", true, null, r.RequestedBy, r.Operation,
                        r.DebitAccountId, r.CreditAccountId, r.Amount.ToString("F2"), r.Currency,
                        "MND-FAKE0000000001", DateTimeOffset.UtcNow),
                    "fake.grant.token",
                    DateTimeOffset.UtcNow.AddSeconds(30));
            },
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        await service.TransferAsync(NewRequest());

        Assert.NotNull(capturedRequest);
        Assert.Equal("Debit", capturedRequest!.Operation);
    }

    [Fact]
    public async Task TransferAsync_WhenLedgerApiReportsAFailedLeg_ReturnsTheFailureReason()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            TransferHandler = _ => new Garbati.OrchestratorApi.Clients.Contracts.Ledger.TransferResponse(
                "TXN-FAILED1", "Failed", "Insufficient funds."),
        };
        var service = CreateService(ctx, identity, ledger);

        var result = await service.TransferAsync(NewRequest());

        Assert.Equal("Failed", result.Status);
        Assert.Equal("Insufficient funds.", result.FailureReason);
    }
}
