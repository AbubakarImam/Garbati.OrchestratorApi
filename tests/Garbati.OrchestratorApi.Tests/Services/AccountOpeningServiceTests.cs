using Garbati.OrchestratorApi.Clients.Contracts.Identity;
using Garbati.OrchestratorApi.Clients.Contracts.Product;
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
        OrchestratorDbContext ctx, FakeIdentityApiClient identity, FakeLedgerApiClient ledger, FakeProductApiClient? product = null) =>
        new(ctx, TestAudit.For(ctx), TestCaller.Default, identity, ledger, product ?? new FakeProductApiClient());

    private static StartAccountOpeningRequest NewRequest(string idempotencyKey) => new(
        idempotencyKey, "Ada Lovelace", "NG", CustomerReference: null,
        Email: "ada@example.com", PhoneNumber: "+2348000000000",
        AccountName: "Ada Savings", AccountType: "Savings", CurrencyCode: "NGN");

    [Fact]
    public async Task StartAsync_HappyPath_RunsAllFiveSteps_AndCompletes()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var product = new FakeProductApiClient();
        var service = CreateService(ctx, identity, ledger, product);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Complete", response.Status);
        Assert.Equal("Complete", response.CurrentStep);
        Assert.NotNull(response.CustomerId);
        Assert.NotNull(response.LedgerAccountId);
        Assert.Equal("NG-SAV-STD", response.ProductCode);
        Assert.Null(response.FailureReason);
        Assert.NotNull(response.CompletedAt);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
        Assert.Equal(1, product.BindAccountToProductCallCount);
    }

    // T-046 task file's test list: "a fake/mock IProductApiClient recorded exactly one call with the
    // expected AccountNumber/ProductCode/idempotency key."
    [Fact]
    public async Task StartAsync_BindsProduct_WithTheExpectedAccountNumberProductCodeAndIdempotencyKey()
    {
        BindAccountToProductRequest? captured = null;
        var product = new FakeProductApiClient
        {
            BindAccountToProductHandler = r =>
            {
                captured = r;
                return new AccountProductBindingResponse(r.AccountNumber, r.ProductCode, DateTimeOffset.UtcNow);
            },
        };
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, new FakeIdentityApiClient(), new FakeLedgerApiClient(), product);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.NotNull(captured);
        Assert.Equal(response.LedgerAccountId, captured!.AccountNumber);
        Assert.Equal("NG-SAV-STD", captured.ProductCode);
        Assert.Equal($"orch:{response.Id}:bind-product", captured.IdempotencyKey);
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
    public async Task StartAsync_WhenAttachMandateFails_StatusIsFailedAtThatStep_AndProductApiIsNeverCalled()
    {
        var identity = new FakeIdentityApiClient
        {
            AttachMandateHandler = (_, _) => throw new DownstreamApiException("IdentityApi", 500, "mandate boom"),
        };
        var ledger = new FakeLedgerApiClient();
        var product = new FakeProductApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger, product);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.Equal("AttachMandate", response.CurrentStep);
        Assert.NotNull(response.CustomerId);
        Assert.NotNull(response.LedgerAccountId);
        Assert.Null(response.ProductCode);
        Assert.Contains("mandate boom", response.FailureReason);

        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
        Assert.Equal(0, product.BindAccountToProductCallCount);
    }

    // T-046 task file's test list: "BindProduct step failure ... leaves CurrentStep == BindProduct,
    // Status == Failed, a clear FailureReason."
    [Fact]
    public async Task StartAsync_WhenBindProductFails_StatusIsFailedAtThatStep()
    {
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var product = new FakeProductApiClient
        {
            BindAccountToProductHandler = _ => throw new DownstreamApiException("ProductApi", 500, "product boom"),
        };
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger, product);

        var response = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.Equal("BindProduct", response.CurrentStep);
        Assert.NotNull(response.CustomerId);
        Assert.NotNull(response.LedgerAccountId);
        Assert.Null(response.ProductCode);
        Assert.Contains("product boom", response.FailureReason);

        Assert.Equal(1, identity.AttachMandateCallCount);
        Assert.Equal(1, product.BindAccountToProductCallCount);
    }

    // T-046 task file's test list: "An AccountType with no mapping ... fails clearly at this step with
    // the specific exception message, not a generic/confusing error."
    [Fact]
    public async Task StartAsync_WhenAccountTypeHasNoProductMapping_FailsClearly_AndProductApiIsNeverCalled()
    {
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var product = new FakeProductApiClient();
        await using var ctx = fixture.CreateContext();
        var service = CreateService(ctx, identity, ledger, product);
        var request = NewRequest(Guid.NewGuid().ToString()) with { AccountType = "Joint" };

        var response = await service.StartAsync(request);

        Assert.Equal("Failed", response.Status);
        Assert.Equal("BindProduct", response.CurrentStep);
        Assert.Equal("No product mapping exists for AccountType 'Joint'.", response.FailureReason);
        Assert.Null(response.ProductCode);
        Assert.Equal(0, product.BindAccountToProductCallCount);
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

    // T-046 task file's test list: "a subsequent retry resumes from BindProduct, not from CreateCustomer
    // (mirror the existing step-failure/retry tests already in this test file for the other three steps
    // exactly)."
    [Fact]
    public async Task RetryAsync_AfterBindProductFailure_ResumesFromBindProduct_WithoutRecallingEarlierSteps()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var product = new FakeProductApiClient
        {
            BindAccountToProductHandler = _ => throw new DownstreamApiException("ProductApi", 500, "product down"),
        };
        var service = CreateService(ctx, identity, ledger, product);

        var failed = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("BindProduct", failed.CurrentStep);
        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
        Assert.Equal(1, product.BindAccountToProductCallCount);

        product.BindAccountToProductHandler = null; // ProductApi recovers

        var retried = await service.RetryAsync(failed.Id);

        Assert.Equal("Complete", retried.Status);
        Assert.Equal(failed.Id, retried.Id);
        Assert.Equal("NG-SAV-STD", retried.ProductCode);
        // Steps 1-4 are NOT re-called - the call-count assertion the task file specifically asks for.
        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
        // Step 5 (the one that failed) is retried.
        Assert.Equal(2, product.BindAccountToProductCallCount);
    }

    [Fact]
    public async Task RetryAsync_WhenAlreadyComplete_IsANoOp_AndDoesNotCallDownstreamAgain()
    {
        await using var ctx = fixture.CreateContext();
        var identity = new FakeIdentityApiClient();
        var ledger = new FakeLedgerApiClient();
        var product = new FakeProductApiClient();
        var service = CreateService(ctx, identity, ledger, product);

        var completed = await service.StartAsync(NewRequest(Guid.NewGuid().ToString()));
        Assert.Equal("Complete", completed.Status);

        var retried = await service.RetryAsync(completed.Id);

        Assert.Equal("Complete", retried.Status);
        Assert.Equal(completed.CompletedAt, retried.CompletedAt);
        Assert.Equal(1, identity.CreateCustomerCallCount);
        Assert.Equal(1, identity.RecordKycDecisionCallCount);
        Assert.Equal(1, ledger.CreateAccountCallCount);
        Assert.Equal(1, identity.AttachMandateCallCount);
        Assert.Equal(1, product.BindAccountToProductCallCount);
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
