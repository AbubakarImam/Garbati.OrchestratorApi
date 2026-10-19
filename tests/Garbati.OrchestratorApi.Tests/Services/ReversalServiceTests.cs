using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Exceptions;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Tests.Fixtures;
using IdentityDto = Garbati.OrchestratorApi.Clients.Contracts.Identity;
using LedgerDto = Garbati.OrchestratorApi.Clients.Contracts.Ledger;

namespace Garbati.OrchestratorApi.Tests.Services;

// Mirrors TransferServiceTests's shape (task file T-030): a denial means LedgerApi is never called; an
// approval calls LedgerApi and reports its outcome back to IdentityApi; a LedgerApi failure after
// authorization still reports WriteOff, not a third state; and a regression test for the exact Operation
// value sent to IdentityApi, guarding against the same class of bug T-024 found and fixed for transfers.
[Collection("Postgres")]
public class ReversalServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static ReversalService CreateService(
        OrchestratorDbContext ctx, FakeIdentityApiClient identity, FakeLedgerApiClient ledger) =>
        new(ctx, TestAudit.For(ctx), identity, ledger);

    private static ReversalRequest NewRequest() => new(
        Guid.NewGuid().ToString(), RequestedBy: "CUS-TEST",
        OriginalTransactionReference: "TXN-ORIGINAL1", Amount: 500m, Currency: "NGN", ReasonCode: "CUST");

    [Fact]
    public async Task ReverseAsync_WhenIdentityApiDenies_DoesNotCallLedgerApi()
    {
        var identity = new FakeIdentityApiClient
        {
            RequestAuthorizationHandler = _ => throw new AuthorizationDeniedException(
                "DEC-DENY-1", "NoOpsReversalScope", "Not authorized to reverse this transaction."),
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(() => service.ReverseAsync(NewRequest()));

        Assert.Equal("DEC-DENY-1", ex.DecisionId);
        Assert.Equal("NoOpsReversalScope", ex.ReasonCode);
        Assert.Equal(1, identity.RequestAuthorizationCallCount);
        Assert.Equal(0, ledger.ReverseCallCount);
        Assert.Equal(0, identity.RecordReversalOutcomeCallCount);
    }

    [Fact]
    public async Task ReverseAsync_WhenApproved_CallsLedgerApi_AndReportsCompletedOutcome()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var service = CreateService(ctx, identity, ledger);

        IdentityDto.RecordReversalOutcomeRequest? reportedOutcome = null;
        identity.RecordReversalOutcomeHandler = (id, r) =>
        {
            reportedOutcome = r;
            return new IdentityDto.ReversalOutcomeResponse(id, r.Outcome, DateTimeOffset.UtcNow, r.RecordedBy, r.Notes);
        };

        var result = await service.ReverseAsync(NewRequest());

        Assert.Equal(1, identity.RequestAuthorizationCallCount);
        Assert.Equal(1, ledger.ReverseCallCount);
        Assert.Equal(1, identity.RecordReversalOutcomeCallCount);
        Assert.StartsWith("DEC-FAKE", result.DecisionId);
        Assert.StartsWith("TXN-FAKE", result.Reference);
        Assert.Equal("Success", result.Status);
        Assert.Null(result.FailureReason);

        Assert.NotNull(reportedOutcome);
        Assert.Equal("Completed", reportedOutcome!.Outcome);
        Assert.Equal("OrchestratorApi", reportedOutcome.RecordedBy);
        Assert.Null(reportedOutcome.Notes);
    }

    [Fact]
    public async Task ReverseAsync_WhenLedgerApiFailsAfterAuthorization_ReportsWriteOffOutcome()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            ReverseHandler = _ => throw new DownstreamApiException(
                "LedgerApi", 422, "Insufficient funds."),
        };
        var service = CreateService(ctx, identity, ledger);

        IdentityDto.RecordReversalOutcomeRequest? reportedOutcome = null;
        identity.RecordReversalOutcomeHandler = (id, r) =>
        {
            reportedOutcome = r;
            return new IdentityDto.ReversalOutcomeResponse(id, r.Outcome, DateTimeOffset.UtcNow, r.RecordedBy, r.Notes);
        };

        var result = await service.ReverseAsync(NewRequest());

        Assert.Equal(1, identity.RequestAuthorizationCallCount);
        Assert.Equal(1, ledger.ReverseCallCount);
        Assert.Equal(1, identity.RecordReversalOutcomeCallCount);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("TXN-ORIGINAL1", result.Reference);
        Assert.Contains("Insufficient funds.", result.FailureReason);

        Assert.NotNull(reportedOutcome);
        Assert.Equal("WriteOff", reportedOutcome!.Outcome);
    }

    [Fact]
    public async Task ReverseAsync_WhenLedgerApiReturns404_ReportsWriteOffOutcome()
    {
        // QA (PR #4): a 404 (original transaction reference not found) is a validly-attempted reversal
        // that could not happen - the same WriteOff bucket as a 422, just a different reason. The original
        // code only caught 422, so a 404 propagated unhandled and IdentityApi's decision was never
        // reported on at all.
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            ReverseHandler = _ => throw new DownstreamApiException(
                "LedgerApi", 404, "Original transaction not found."),
        };
        var service = CreateService(ctx, identity, ledger);

        IdentityDto.RecordReversalOutcomeRequest? reportedOutcome = null;
        identity.RecordReversalOutcomeHandler = (id, r) =>
        {
            reportedOutcome = r;
            return new IdentityDto.ReversalOutcomeResponse(id, r.Outcome, DateTimeOffset.UtcNow, r.RecordedBy, r.Notes);
        };

        var result = await service.ReverseAsync(NewRequest());

        Assert.Equal(1, identity.RecordReversalOutcomeCallCount);
        Assert.Equal("Failed", result.Status);
        Assert.Equal("TXN-ORIGINAL1", result.Reference);
        Assert.Contains("Original transaction not found.", result.FailureReason);

        Assert.NotNull(reportedOutcome);
        Assert.Equal("WriteOff", reportedOutcome!.Outcome);
    }

    [Fact]
    public async Task ReverseAsync_WhenLedgerApiReturns409_ReportsCompletedOutcome_NotWriteOff()
    {
        // QA's exact reproduction (PR #4): a 409 is LedgerApi's own duplicate-reversal guard, meaning this
        // transaction was already reversed by a prior attempt whose response the caller never saw
        // (lost-response-then-retry). The money WAS moved - this must report Completed, not WriteOff and
        // not an unhandled error, or IdentityApi's decision is left dangling forever on every retry.
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            ReverseHandler = _ => throw new DownstreamApiException(
                "LedgerApi", 409, "This transaction has already been reversed."),
        };
        var service = CreateService(ctx, identity, ledger);

        IdentityDto.RecordReversalOutcomeRequest? reportedOutcome = null;
        identity.RecordReversalOutcomeHandler = (id, r) =>
        {
            reportedOutcome = r;
            return new IdentityDto.ReversalOutcomeResponse(id, r.Outcome, DateTimeOffset.UtcNow, r.RecordedBy, r.Notes);
        };

        var result = await service.ReverseAsync(NewRequest());

        Assert.Equal(1, identity.RecordReversalOutcomeCallCount);
        Assert.Equal("Success", result.Status);
        Assert.Null(result.FailureReason);

        Assert.NotNull(reportedOutcome);
        Assert.Equal("Completed", reportedOutcome!.Outcome);
    }

    [Fact]
    public async Task ReverseAsync_WhenLedgerApiFailsWithAnUnexpectedError_StillReportsAnUnconfirmedWriteOffOutcome_ButRethrowsTheOriginalError()
    {
        // QA (PR #4): a 500 (or any other unexpected failure) means we genuinely do not know whether
        // LedgerApi's reversal happened. The fix must still report *something* to IdentityApi (WriteOff,
        // flagged "Unconfirmed" in Notes for manual reconciliation) rather than leaving the
        // AuthorizationDecision dangling forever - but the original error must still surface to our own
        // caller, not be swallowed by a fabricated success.
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            ReverseHandler = _ => throw new DownstreamApiException(
                "LedgerApi", 500, "Internal server error."),
        };
        var service = CreateService(ctx, identity, ledger);

        IdentityDto.RecordReversalOutcomeRequest? reportedOutcome = null;
        identity.RecordReversalOutcomeHandler = (id, r) =>
        {
            reportedOutcome = r;
            return new IdentityDto.ReversalOutcomeResponse(id, r.Outcome, DateTimeOffset.UtcNow, r.RecordedBy, r.Notes);
        };

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => service.ReverseAsync(NewRequest()));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(1, identity.RecordReversalOutcomeCallCount);

        Assert.NotNull(reportedOutcome);
        Assert.Equal("WriteOff", reportedOutcome!.Outcome);
        Assert.NotNull(reportedOutcome.Notes);
        Assert.Contains("Unconfirmed", reportedOutcome.Notes);
        Assert.Contains("500", reportedOutcome.Notes);
    }

    [Fact]
    public async Task ReverseAsync_WhenLedgerApiFailsUnexpectedly_AndReportingTheOutcomeAlsoFails_StillRethrowsTheOriginalError()
    {
        // The outcome-report call itself failing must not mask the original LedgerApi failure by throwing
        // a different, unhandled exception on top of it (QA's "make sure... it doesn't throw unhandled
        // and mask the original problem").
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient
        {
            RecordReversalOutcomeHandler = (_, _) => throw new DownstreamApiException(
                "IdentityApi", 503, "IdentityApi is unavailable."),
        };
        var ledger = new FakeLedgerApiClient
        {
            ReverseHandler = _ => throw new DownstreamApiException(
                "LedgerApi", 500, "Internal server error."),
        };
        var service = CreateService(ctx, identity, ledger);

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => service.ReverseAsync(NewRequest()));

        Assert.Equal("LedgerApi", ex.ServiceName);
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(1, identity.RecordReversalOutcomeCallCount);
    }

    [Fact]
    public async Task ReverseAsync_SendsReversalAsTheOperation_AndOriginalTransactionReferenceAsDebitAccountId()
    {
        // T-024-shaped regression test (task file T-030): assert the exact AuthorizationRequest fields
        // sent to IdentityApi, not just that it was called - the same gap that let a wrong Operation
        // literal ship for transfers undetected.
        IdentityDto.AuthorizationRequest? capturedRequest = null;
        var identity = new FakeIdentityApiClient
        {
            RequestAuthorizationHandler = r =>
            {
                capturedRequest = r;
                return new IdentityDto.AuthorizationResponse(
                    new IdentityDto.AuthorizationDecisionResponse(
                        "DEC-FAKE0000000001", true, null, r.RequestedBy, r.Operation,
                        r.DebitAccountId, r.CreditAccountId, r.Amount.ToString("F2"), r.Currency,
                        null, DateTimeOffset.UtcNow),
                    "fake.grant.token",
                    DateTimeOffset.UtcNow.AddSeconds(30));
            },
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var request = NewRequest();
        await service.ReverseAsync(request);

        Assert.NotNull(capturedRequest);
        Assert.Equal("Reversal", capturedRequest!.Operation);
        Assert.Equal(request.OriginalTransactionReference, capturedRequest.DebitAccountId);
        Assert.Null(capturedRequest.CreditAccountId);
        Assert.Equal(request.ReasonCode, capturedRequest.ReasonCode);
        Assert.Equal(request.Amount, capturedRequest.Amount);
        Assert.Equal(request.Currency, capturedRequest.Currency);
        Assert.Equal(request.IdempotencyKey, capturedRequest.IdempotencyKey);
    }

    [Fact]
    public void ReversalResultResponse_HasNoGrantField()
    {
        // Same guarantee TransferResultResponse's own test asserts.
        var propertyNames = typeof(Garbati.OrchestratorApi.Contracts.Responses.ReversalResultResponse).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(propertyNames, name => name.Contains("Grant", StringComparison.OrdinalIgnoreCase));
    }
}
