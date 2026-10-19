using Garbati.OrchestratorApi.Clients.Contracts.Identity;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Exceptions;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Garbati.OrchestratorApi.Tests.Services;

// Task file's test list for account opening: the full happy path, a failure at each step (with nothing
// downstream of the failure attempted), retry resuming from the failed step (not step 1), retrying an
// already-complete operation as a no-op, and the idempotency convention on POST itself.
[Collection("Postgres")]
public class AccountOpeningServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static AccountOpeningService CreateService(
        OrchestratorDbContext ctx, FakeIdentityApiClient identity, FakeLedgerApiClient ledger) =>
        new(ctx, TestAudit.For(ctx), TestCaller.Default, identity, ledger);

    private static StartAccountOpeningRequest NewRequest(string idempotencyKey) => new(
        idempotencyKey, "Ada Lovelace", "NG", CustomerReference: null,
        Email: "ada@example.com", PhoneNumber: "+2348000000000",
        AccountName: "Ada Savings", AccountType: "Savings", CurrencyCode: "NGN");

    [Fact]
    public async Task StartAsync_HappyPath_RunsAllFourSteps_AndCompletes()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var service = CreateService(ctx, identity, ledger);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Complete", response.Status);
        Assert.Equal("Complete", response.CurrentStep);
        Assert.NotNull(response.CustomerId);
        Assert.NotNull(response.LedgerAccountId);
        Assert.Null(response.FailureReason);
        Assert.NotNull(response.CompletedAt);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
    }

    // T-025 regression: the demo KYC stub must assign a tier that IdentityApi's own tier-policy data
    // actually allows Debit/Credit for, or no demo-opened account can ever pass a transfer's authorization
    // check (confirmed live: a Tier1 demo account's transfer attempt came back denied with IdentityApi's
    // real OperationNotEligibleForKycTier reason, since IdentityApi's "NG" seed data lists Tier1 as
    // View-only). This asserts the actual captured request value, not just that the call happened - the
    // same gap (mock invoked, nobody checked what was sent) is exactly what let the Tier1 mismatch ship.
    [Fact]
    public async Task StartAsync_RecordsKycDecision_WithATierThatAllowsDebitAndCredit()
    {
        KycDecisionRequest? captured = null;
        var identity = new FakeIdentityApiClient
        {
            RecordKycDecisionHandler = (id, r) =>
            {
                captured = r;
                return new CustomerResponse(id, null, "Active", r.KycStatus, r.KycTier, "NG", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            },
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.NotNull(captured);
        Assert.Equal("Tier2", captured!.KycTier);
    }

    [Fact]
    public async Task StartAsync_WhenCreateCustomerFails_StatusIsFailedAtThatStep_AndNothingElseIsCalled()
    {
        var identity = new FakeIdentityApiClient
        {
            CreateCustomerHandler = _ => throw new DownstreamApiException("IdentityApi", 500, "boom"),
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.Equal("CreateCustomer", response.CurrentStep);
        Assert.Contains("boom", response.FailureReason);
        Assert.Null(response.CustomerId);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(0, identity.RecordKycDecisionCallCount);
        Assert.Equal(0, ledger.CreateAccountCallCount);
        Assert.Equal(0, identity.AttachMandateCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenVerifyKycFails_StatusIsFailedAtThatStep_AndLedgerIsNeverCalled()
    {
        var identity = new FakeIdentityApiClient
        {
            RecordKycDecisionHandler = (_, _) => throw new DownstreamApiException("IdentityApi", 500, "kyc boom"),
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.Equal("VerifyKyc", response.CurrentStep);
        Assert.NotNull(response.CustomerId); // step 1 already succeeded and is recorded
        Assert.Contains("kyc boom", response.FailureReason);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(0, ledger.CreateAccountCallCount);
        Assert.Equal(0, identity.AttachMandateCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenCreateLedgerAccountFails_StatusIsFailedAtThatStep_AndMandateIsNeverAttached()
    {
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            CreateAccountHandler = _ => throw new DownstreamApiException("LedgerApi", 500, "ledger down"),
        };
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.Equal("CreateLedgerAccount", response.CurrentStep);
        Assert.Null(response.LedgerAccountId);
        Assert.Contains("ledger down", response.FailureReason);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(0, identity.AttachMandateCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenAttachMandateFails_StatusIsFailedAtThatStep()
    {
        var identity = new FakeIdentityApiClient
        {
            AttachMandateHandler = (_, _) => throw new DownstreamApiException("IdentityApi", 500, "mandate boom"),
        };
        var ledger = new FakeLedgerApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.Equal("AttachMandate", response.CurrentStep);
        Assert.NotNull(response.CustomerId);
        Assert.NotNull(response.LedgerAccountId);
        Assert.Contains("mandate boom", response.FailureReason);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
    }

    [Fact]
    public async Task RetryAsync_AfterStep3Failure_ResumesFromStep3_WithoutRecallingSteps1And2()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient
        {
            CreateAccountHandler = _ => throw new DownstreamApiException("LedgerApi", 500, "ledger down"),
        };
        var service = CreateService(ctx, identity, ledger);

        var failed = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("CreateLedgerAccount", failed.CurrentStep);
        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(0, identity.AttachMandateCallCount);

        ledger.CreateAccountHandler = null; // LedgerApi recovers

        var retried = await service.RetryAsync(failed.Id);

        Assert.Equal("Complete", retried.Status);
        Assert.Equal(failed.Id, retried.Id);
        // Steps 1-2 are NOT re-called - this is the call-count assertion the task file specifically asks for.
        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        // Step 3 (the one that failed) is retried.
        Assert.Equal(2, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
    }

    [Fact]
    public async Task RetryAsync_WhenAlreadyComplete_IsANoOp_AndDoesNotCallDownstreamAgain()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var service = CreateService(ctx, identity, ledger);

        var completed = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));
        Assert.Equal("Complete", completed.Status);

        var retried = await service.RetryAsync(completed.Id);

        Assert.Equal("Complete", retried.Status);
        Assert.Equal(completed.CompletedAt, retried.CompletedAt);
        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
    }

    [Fact]
    public async Task RetryAsync_WhenOperationDoesNotExist_ThrowsNotFound()
    {
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, new FakeIdentityApiClient(), new FakeLedgerApiClient());

        await Assert.ThrowsAsync<AccountOpeningNotFoundException>(() => service.RetryAsync("OPN-DOESNOTEXIST"));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenOperationDoesNotExist()
    {
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, new FakeIdentityApiClient(), new FakeLedgerApiClient());

        Assert.Null(await service.GetAsync("OPN-DOESNOTEXIST"));
    }

    [Fact]
    public async Task StartAsync_RetriedWithSameKeyAndSameBody_ReturnsTheSameOperation_NotASecondOne()
    {
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, new FakeIdentityApiClient(), new FakeLedgerApiClient());
        var request = NewRequest(Guid.NewGuid().ToString());

        var first = await service.StartAsync(request);
        var second = await service.StartAsync(request);

        Assert.Equal(first.Id, second.Id);

        await using var verifyCtx = fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.AccountOpenings.CountAsync());
    }

    [Fact]
    public async Task StartAsync_RetriedWithSameKeyButDifferentBody_ThrowsDuplicateIdempotencyKey()
    {
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, new FakeIdentityApiClient(), new FakeLedgerApiClient());
        var key = Guid.NewGuid().ToString();

        await service.StartAsync(NewRequest(key));
        var differentBody = NewRequest(key) with { FullName = "A Different Name" };

        await Assert.ThrowsAsync<DuplicateIdempotencyKeyException>(() => service.StartAsync(differentBody));
    }
}
