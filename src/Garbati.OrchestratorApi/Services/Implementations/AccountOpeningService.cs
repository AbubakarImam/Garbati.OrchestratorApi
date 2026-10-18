using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Garbati.OrchestratorApi.Auditing;
using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Clients.Contracts.Identity;
using Garbati.OrchestratorApi.Clients.Contracts.Ledger;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Entities;
using Garbati.OrchestratorApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Garbati.OrchestratorApi.Services;

// DESIGN.md Section 1.3/1.4/1.11, task file Section 1. Runs the 4-step account-opening sequence
// synchronously in one call, tracking progress in the AccountOpenings table so a retry resumes from
// CurrentStep rather than redoing steps that already succeeded.
public class AccountOpeningService(
    OrchestratorDbContext dbContext,
    IAuditLogger auditLogger,
    ICurrentClient currentClient,
    IIdentityApiClient identityApiClient,
    ILedgerApiClient ledgerApiClient) : IAccountOpeningService
{
    private const int MaxOperationIdGenerationAttempts = 5;

    // Demo KYC stub (DESIGN.md decision #6, task file Section 1 step 2): "a hardcoded, clearly-labeled
    // demo stub decision ... this is explicitly a demo stub, not real verification." No real KYC provider
    // integration exists anywhere in this platform yet - this is not a shortcut unique to the
    // Orchestrator, it is the one decided way v1 satisfies IdentityApi's own KYC-verification requirement
    // before a mandate/authorization check will treat the customer as eligible at all.
    //
    // KycTier "Tier2" (T-025 - was "Tier1" until IdentityApi's placeholder KycTierPolicy seed data showed
    // that was wrong). IdentityApi's "NG" seed migration only lists "View" for Tier1, so an account opened
    // at Tier1 can never pass MandateEligibility's Debit/Credit check - and T-022's own demo scope is
    // explicitly "account opening, plain transfer", which needs Debit to work. Tier2 lists
    // View/Credit/Debit, so this is the lowest tier that actually supports the demo path it exists for.
    // Still a hardcoded, clearly-labeled demo stub decision, not a real KYC-tier policy choice - a real
    // deployment would need a real tier decision per customer/jurisdiction, which is exactly what a real
    // KYC provider would supply instead of this stub. (Also still assumes the target jurisdiction has a
    // Tier2 KycTierPolicy row seeded on the IdentityApi side - true for "NG" in its current seed
    // migration, not guaranteed for an arbitrary JurisdictionCode a caller supplies.)
    private const string DemoKycTier = "Tier2";
    private const string DemoKycVerificationType = "DemoStub";
    private const string DemoKycProvider = "OrchestratorDemoStub-NotRealVerification";

    // Fingerprint of the fields that define the operation being started, so a reused idempotency key with
    // a different body is detected (same scheme as LedgerApi's AccountService/IdentityApi's
    // CustomerService: decision #2 here mirrors decisions #43/#29 there).
    private static string HashRequest(StartAccountOpeningRequest request) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[]
        {
            request.FullName,
            request.JurisdictionCode,
            request.CustomerReference ?? "",
            request.Email ?? "",
            request.PhoneNumber ?? "",
            request.AccountName,
            request.AccountType,
            request.CurrencyCode,
        }))));

    private static AccountOpeningResponse ToResponse(AccountOpening opening) => new(
        opening.OperationId,
        opening.Status.ToString(),
        opening.CurrentStep.ToString(),
        opening.CustomerId,
        opening.LedgerAccountId,
        opening.FailureReason,
        opening.CreatedAt,
        opening.CompletedAt);

    public async Task<AccountOpeningResponse> StartAsync(StartAccountOpeningRequest request, CancellationToken cancellationToken = default)
    {
        var clientId = currentClient.ClientId;
        var requestHash = HashRequest(request);

        for (var attempt = 1; attempt <= MaxOperationIdGenerationAttempts; attempt++)
        {
            var now = DateTimeOffset.UtcNow;

            var opening = new AccountOpening
            {
                OperationId = OperationIdGenerator.Generate(),
                IdempotencyKey = request.IdempotencyKey,
                InitiatedBy = clientId,
                RequestHash = requestHash,
                FullName = request.FullName,
                JurisdictionCode = request.JurisdictionCode,
                CustomerReference = request.CustomerReference,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                AccountName = request.AccountName,
                AccountType = request.AccountType,
                CurrencyCode = request.CurrencyCode,
                CurrentStep = AccountOpeningStep.CreateCustomer,
                Status = AccountOpeningStatus.InProgress,
                CreatedAt = now,
            };

            await dbContext.AccountOpenings.AddAsync(opening, cancellationToken);

            var audit = auditLogger.Record(new AuditLog
            {
                Action = AuditActions.AccountOpeningStarted,
                EntityType = nameof(AccountOpening),
                EntityId = opening.OperationId,
                Status = opening.Status.ToString(),
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx)
            {
                // Dependents before principals (EF navigation-fixup ordering - mirrors CustomerService).
                dbContext.Entry(opening).State = EntityState.Detached;
                dbContext.Entry(audit).State = EntityState.Detached;

                if (pgEx.ConstraintName == "ix_account_openings_initiated_by_idempotency_key")
                {
                    var existing = await dbContext.AccountOpenings.AsNoTracking().FirstOrDefaultAsync(
                        o => o.InitiatedBy == clientId && o.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                    if (existing is null)
                        throw new InvalidOperationException("Error retrieving the result of the existing idempotency key.");

                    // Same key, different request: refuse rather than report the old operation as this
                    // one's (409, ARCHITECTURE.md Section 5).
                    if (existing.RequestHash != requestHash)
                        throw new DuplicateIdempotencyKeyException(
                            $"Idempotency key '{request.IdempotencyKey}' was already used for a different request.");

                    // Same key, same request: this is a retry of an already-started (or already-finished)
                    // operation. Resume/no-op through the same path POST .../retry uses, rather than
                    // silently returning a possibly-stale snapshot.
                    return await RunFromCurrentStepAsync(await GetTrackedAsync(existing.OperationId, cancellationToken), cancellationToken);
                }
                // Otherwise the collision was on OperationId: loop around and try another one.
                continue;
            }

            return await RunFromCurrentStepAsync(opening, cancellationToken);
        }

        throw new InvalidOperationException($"Could not allocate a unique OperationId after {MaxOperationIdGenerationAttempts} attempts.");
    }

    public async Task<AccountOpeningResponse?> GetAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var opening = await dbContext.AccountOpenings.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, cancellationToken);
        return opening is null ? null : ToResponse(opening);
    }

    public async Task<AccountOpeningResponse> RetryAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var opening = await GetTrackedAsync(operationId, cancellationToken);
        return await RunFromCurrentStepAsync(opening, cancellationToken);
    }

    private async Task<AccountOpening> GetTrackedAsync(string operationId, CancellationToken cancellationToken) =>
        await dbContext.AccountOpenings.FirstOrDefaultAsync(o => o.OperationId == operationId, cancellationToken)
            ?? throw new AccountOpeningNotFoundException($"Account opening '{operationId}' was not found.");

    // DESIGN.md Section 1.4: "re-runs the sequence from CurrentStep onward ... every downstream call uses
    // a derived, deterministic idempotency key ... so a step that already succeeded is a safe no-op if
    // accidentally re-invoked." Each `if` below falls through to the next once its step's CurrentStep is
    // advanced, so a fresh Start runs steps 1-4 in one pass; a retry starting mid-sequence only enters the
    // blocks at or after the stored CurrentStep - the skipped blocks are never reached, which is what the
    // task file's "verify with a call-count assertion" tests check.
    private async Task<AccountOpeningResponse> RunFromCurrentStepAsync(AccountOpening opening, CancellationToken cancellationToken)
    {
        // Decision #2 / task file: "If Status is already Complete, this is a no-op that just returns the
        // current state (don't error - matches ... Customer.Suspend/Mandate.Revoke['s]... 'repeat of an
        // already-successful thing is idempotent' convention."
        if (opening.Status == AccountOpeningStatus.Complete)
        {
            return ToResponse(opening);
        }

        opening.Status = AccountOpeningStatus.InProgress;
        opening.FailureReason = null;

        try
        {
            if (opening.CurrentStep == AccountOpeningStep.CreateCustomer)
            {
                var customer = await identityApiClient.CreateCustomerAsync(
                    new CreateCustomerRequest(
                        opening.FullName, opening.JurisdictionCode, opening.CustomerReference, opening.Email, opening.PhoneNumber,
                        $"orch:{opening.OperationId}:create-customer"),
                    cancellationToken);
                opening.CustomerId = customer.CustomerId;
                opening.CurrentStep = AccountOpeningStep.VerifyKyc;
                await SaveStepProgressAsync(opening, "CreateCustomer succeeded", cancellationToken);
            }

            if (opening.CurrentStep == AccountOpeningStep.VerifyKyc)
            {
                await identityApiClient.RecordKycDecisionAsync(
                    opening.CustomerId!,
                    new KycDecisionRequest(
                        KycStatus: "Verified", KycTier: DemoKycTier, VerificationType: DemoKycVerificationType,
                        Provider: DemoKycProvider, ProviderReference: null, ResultCode: "DEMO-AUTO-APPROVED", ExpiresAt: null),
                    cancellationToken);
                opening.CurrentStep = AccountOpeningStep.CreateLedgerAccount;
                await SaveStepProgressAsync(opening, "VerifyKyc succeeded (demo stub)", cancellationToken);
            }

            if (opening.CurrentStep == AccountOpeningStep.CreateLedgerAccount)
            {
                var account = await ledgerApiClient.CreateAccountAsync(
                    new CreateAccountRequest(
                        opening.AccountName, opening.AccountType, opening.CurrencyCode,
                        $"orch:{opening.OperationId}:create-account"),
                    cancellationToken);
                opening.LedgerAccountId = account.AccountNumber;
                opening.CurrentStep = AccountOpeningStep.AttachMandate;
                await SaveStepProgressAsync(opening, "CreateLedgerAccount succeeded", cancellationToken);
            }

            if (opening.CurrentStep == AccountOpeningStep.AttachMandate)
            {
                try
                {
                    await identityApiClient.AttachMandateAsync(
                        opening.CustomerId!,
                        new AttachMandateRequest(opening.LedgerAccountId!, ["View", "Debit", "Credit"]),
                        cancellationToken);
                }
                catch (MandateAlreadyAttachedException)
                {
                    // Already attached by an earlier attempt at this same step (Exceptions/
                    // MandateAlreadyAttachedException.cs) - treated as success on retry, not re-thrown.
                }
                opening.CurrentStep = AccountOpeningStep.Complete;
                await SaveStepProgressAsync(opening, "AttachMandate succeeded", cancellationToken);
            }

            opening.Status = AccountOpeningStatus.Complete;
            opening.CompletedAt = DateTimeOffset.UtcNow;
            auditLogger.Record(new AuditLog
            {
                Action = AuditActions.AccountOpeningCompleted,
                EntityType = nameof(AccountOpening),
                EntityId = opening.OperationId,
                Status = opening.Status.ToString(),
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // DESIGN.md 1.11/task file: "If any step throws, catch it, set Status = Failed,
            // FailureReason, leave CurrentStep at whichever step failed, and return a response ... do not
            // let an unhandled exception escape as a bare 500 with no operation record."
            opening.Status = AccountOpeningStatus.Failed;
            opening.FailureReason = ex.Message;

            auditLogger.Record(new AuditLog
            {
                Action = AuditActions.AccountOpeningStepFailed,
                EntityType = nameof(AccountOpening),
                EntityId = opening.OperationId,
                Status = opening.Status.ToString(),
                Details = $"Step {opening.CurrentStep}: {ex.Message}",
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(opening);
    }

    private Task SaveStepProgressAsync(AccountOpening opening, string details, CancellationToken cancellationToken)
    {
        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.AccountOpeningStepSucceeded,
            EntityType = nameof(AccountOpening),
            EntityId = opening.OperationId,
            Status = opening.CurrentStep.ToString(),
            Details = details,
        });
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
