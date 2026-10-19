using Garbati.OrchestratorApi.Auditing;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Entities;
using Garbati.OrchestratorApi.Exceptions;
// Aliased, not "using"-imported directly: same reason as TransferService - both IdentityDto and LedgerDto
// define their own Reversal-shaped types, which would otherwise collide with each other and with this
// service's own Contracts.Requests.ReversalRequest.
using IdentityDto = Garbati.OrchestratorApi.Clients.Contracts.Identity;
using LedgerDto = Garbati.OrchestratorApi.Clients.Contracts.Ledger;

namespace Garbati.OrchestratorApi.Services;

// T-030 (garbati-platform D-14). Mirrors TransferService's own shape, exception handling and audit-log
// pattern exactly, with one addition only a reversal needs: reporting the real LedgerApi outcome back to
// IdentityApi afterwards (step 3 below). IdentityApi's own design (Section 8) anticipated this call
// coming from "the ops channel (or the Orchestrator)" - this task settles it as the Orchestrator, since
// it is the service that directly observes LedgerApi's real result, not a relay of something another
// service saw.
//
// Same as TransferService: no new persistent entity for a reversal itself - OrchestratorDbContext is
// used only to persist the audit trail, the same AuditLogs table account opening and transfer use.
public class ReversalService(
    OrchestratorDbContext dbContext,
    IAuditLogger auditLogger,
    IIdentityApiClient identityApiClient,
    ILedgerApiClient ledgerApiClient) : IReversalOrchestrationService
{
    public async Task<ReversalResultResponse> ReverseAsync(
        ReversalRequest request, CancellationToken cancellationToken = default)
    {
        IdentityDto.AuthorizationResponse authorization;
        try
        {
            // Operation: "Reversal" - IdentityApi's AuthorizationRequestValidator accepts this literal as
            // a second, separate kind of Operation alongside the MandateOperation values TransferService
            // uses (View/Debit/Credit) - a reversal never touches a mandate at all (IdentityApi's own
            // decision #31), so it is never "Debit" here the way a transfer is.
            // DebitAccountId: request.OriginalTransactionReference - IdentityApi's own documented
            // convention (AuthorizationRequest's own comment): "DebitAccountId itself carries that single
            // reference for a Reversal request ... there is no dedicated 'original transaction
            // reference' field."
            // CreditAccountId: null - a reversal names no destination account.
            // Amount/Currency/IdempotencyKey: passed straight through, same as TransferService.
            // ReasonCode: request.ReasonCode - ignored by IdentityApi for every operation except Reversal.
            authorization = await identityApiClient.RequestAuthorizationAsync(
                new IdentityDto.AuthorizationRequest(
                    request.RequestedBy, "Reversal", request.OriginalTransactionReference, null,
                    request.Amount, request.Currency, request.IdempotencyKey, request.ReasonCode),
                cancellationToken);
        }
        catch (AuthorizationDeniedException denied)
        {
            // Same handling TransferService already has: stop here, do not call LedgerApi. Rethrown for
            // ExceptionHandlingMiddleware to map to 422 with the decision id/reason code attached.
            auditLogger.Record(new AuditLog
            {
                Action = AuditActions.ReversalDenied,
                EntityType = "Reversal",
                EntityId = denied.DecisionId ?? request.IdempotencyKey,
                Status = "Denied",
                Details = denied.ReasonCode,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.ReversalAuthorized,
            EntityType = "Reversal",
            EntityId = authorization.Decision.DecisionId,
            Status = "Approved",
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // Grant-token forwarding/local verification is still deliberately deferred everywhere on this
        // platform (decision #47/D-6) - `authorization.Grant` is never referenced here either, same as
        // TransferService.
        string reference;
        string status;
        string? failureReason;
        string outcome;
        try
        {
            var reversal = await ledgerApiClient.ReverseAsync(
                new LedgerDto.ReversalRequest(request.OriginalTransactionReference, request.ReasonCode),
                cancellationToken);
            reference = reversal.Reference;
            status = reversal.Status;
            failureReason = null;
            outcome = "Completed";
        }
        catch (DownstreamApiException ex) when (
            ex.StatusCode is StatusCodes.Status404NotFound or StatusCodes.Status422UnprocessableEntity)
        {
            // LedgerApi's reversal call throws rather than returning a Status="Failed" body the way
            // TransferAsync does (see ReverseAsync's own comment). Both 404 (bad OriginalTransactionReference
            // - nothing to reverse) and 422 (a business rule, e.g. insufficient funds on the account being
            // reversed from) mean the reversal was validly attempted against this decision but could not
            // happen - exactly the "money not recovered" case WriteOff exists for (decision #34,
            // reconfirmed by T-021) - not a third state to invent. QA (PR #4) found the original code only
            // caught 422; 404 belongs in the same bucket, not the unconfirmed one below, since LedgerApi
            // has definitively told us this reversal did not happen.
            reference = request.OriginalTransactionReference;
            status = "Failed";
            failureReason = ex.Message;
            outcome = "WriteOff";
        }
        catch (DownstreamApiException ex) when (ex.StatusCode == StatusCodes.Status409Conflict)
        {
            // 409 is LedgerApi's own duplicate-reversal guard: this exact transaction was already
            // reversed by a prior attempt. QA's exact reproduction (PR #4 review) is the
            // lost-response-then-retry scenario this exists for - the money WAS moved, just not by THIS
            // call, so report Completed (not WriteOff, not an error) back to IdentityApi.
            reference = request.OriginalTransactionReference;
            status = "Success";
            failureReason = null;
            outcome = "Completed";
        }
        catch (Exception ex)
        {
            // QA (PR #4): everything else - a 500, a timeout, any other unexpected failure, including a
            // non-DownstreamApiException such as a transport-level exception - genuinely does not tell us
            // whether LedgerApi's reversal happened. Leaving IdentityApi's AuthorizationDecision dangling
            // forever is exactly the bug QA proved out (and IdentityApi's ReversalAuthorizationService has
            // no idempotency dedup to make a bare retry safe either, a separate pre-existing gap on its
            // side). So: still report an outcome - WriteOff, since nothing was confirmed recovered - but
            // flag it as unconfirmed in Notes so a human reviewing ReversalOutcomes later can tell a
            // confirmed business-rule WriteOff apart from a genuinely uncertain one. Then still surface the
            // original error to our own caller (do not fabricate a settled result) - the report call
            // itself is wrapped below so that if IT also fails, that failure is audited, not thrown
            // unhandled on top of (and masking) the original LedgerApi failure.
            var detail = ex is DownstreamApiException downstream
                ? $"status {downstream.StatusCode}"
                : $"{ex.GetType().Name}: {ex.Message}";
            try
            {
                await identityApiClient.RecordReversalOutcomeAsync(
                    authorization.Decision.DecisionId,
                    new IdentityDto.RecordReversalOutcomeRequest(
                        "WriteOff", "OrchestratorApi",
                        $"Unconfirmed: LedgerApi call failed with {detail} - needs manual reconciliation"),
                    cancellationToken);
            }
            catch (Exception reportEx)
            {
                auditLogger.Record(new AuditLog
                {
                    Action = AuditActions.ReversalOutcomeReportFailed,
                    EntityType = "Reversal",
                    EntityId = authorization.Decision.DecisionId,
                    Status = "Failed",
                    Details = reportEx.Message,
                });
            }

            auditLogger.Record(new AuditLog
            {
                Action = AuditActions.ReversalPosted,
                EntityType = "Reversal",
                EntityId = request.OriginalTransactionReference,
                Status = "Unconfirmed",
                Details = $"DecisionId {authorization.Decision.DecisionId}; LedgerApi failure: {ex.Message}",
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            throw;
        }

        // Report the real outcome back to IdentityApi (already built, T-020) - RecordedBy/Notes are fixed
        // for v1 (task file): nothing richer than the outcome itself is captured yet. (The genuinely
        // unknown/unconfirmed path above reports its own outcome, with Notes, and returns before this
        // point - it never reaches here.)
        await identityApiClient.RecordReversalOutcomeAsync(
            authorization.Decision.DecisionId,
            new IdentityDto.RecordReversalOutcomeRequest(outcome, "OrchestratorApi", null),
            cancellationToken);

        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.ReversalPosted,
            EntityType = "Reversal",
            EntityId = reference,
            Status = status,
            Details = $"DecisionId {authorization.Decision.DecisionId}",
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReversalResultResponse(
            authorization.Decision.DecisionId, reference, status, failureReason);
    }
}
