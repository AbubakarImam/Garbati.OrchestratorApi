using Garbati.OrchestratorApi.Auditing;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Entities;
using Garbati.OrchestratorApi.Exceptions;
// Aliased, not "using"-imported directly: both IdentityDto and LedgerDto define their own
// AuthorizationRequest/TransferRequest-shaped types, which would otherwise collide with each other and
// with this service's own Contracts.Requests.TransferRequest.
using IdentityDto = Garbati.OrchestratorApi.Clients.Contracts.Identity;
using LedgerDto = Garbati.OrchestratorApi.Clients.Contracts.Ledger;

namespace Garbati.OrchestratorApi.Services;

// DESIGN.md Section 1.5/1.6/1.7/1.11. No new persistent entity for a transfer itself (decision #1/Section
// 1.5: "both downstream calls are already independently idempotent") - OrchestratorDbContext is used only
// to persist the audit trail Section 1.12 requires, the same AuditLogs table account opening uses.
public class TransferService(
    OrchestratorDbContext dbContext,
    IAuditLogger auditLogger,
    IIdentityApiClient identityApiClient,
    ILedgerApiClient ledgerApiClient) : ITransferOrchestrationService
{
    public async Task<TransferResultResponse> TransferAsync(
        TransferRequest request, CancellationToken cancellationToken = default)
    {
        IdentityDto.AuthorizationResponse authorization;
        try
        {
            // DESIGN.md 1.6: RequestedBy is the caller-supplied, unauthenticated-behind-it CustomerId -
            // passed straight through, never treated as a verified identity by this service.
            // "Debit", not "Transfer": IdentityApi's AuthorizationRequestValidator only accepts
            // MandateOperation values (View/Debit/Credit) or the literal "Reversal" - a transfer is, from
            // IdentityApi's point of view, exactly "may this requester debit this account for this
            // amount?" (DESIGN.md Section 2.2's mandate-permission vocabulary). "Transfer" is not a
            // recognized Operation and was rejected with a 400 on every live call (T-024).
            authorization = await identityApiClient.RequestAuthorizationAsync(
                new IdentityDto.AuthorizationRequest(
                    request.RequestedBy, "Debit", request.DebitAccountId, request.CreditAccountId,
                    request.Amount, request.Currency, request.IdempotencyKey),
                cancellationToken);
        }
        catch (AuthorizationDeniedException denied)
        {
            // DESIGN.md 1.11: "IdentityApi denies authorization for a transfer -> Stop; do not call
            // LedgerApi. Return the denial reason, not a fabricated success." Rethrown for
            // ExceptionHandlingMiddleware to map to 422 with the decision id/reason code attached.
            auditLogger.Record(new AuditLog
            {
                Action = AuditActions.TransferDenied,
                EntityType = "Transfer",
                EntityId = denied.DecisionId ?? request.IdempotencyKey,
                Status = "Denied",
                Details = denied.ReasonCode,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.TransferAuthorized,
            EntityType = "Transfer",
            EntityId = authorization.Decision.DecisionId,
            Status = "Approved",
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // DESIGN.md 1.7/D-6: the grant is deliberately NOT forwarded into this call - LedgerApi's
        // TransferRequest has no field for it yet (decision #47 is still deferred), and this version does
        // not independently re-verify it either (see the PR description for that judgment call).
        // `authorization.Grant` is read by IdentityApiClient and never touches this method at all - it is
        // simply never referenced, so it cannot leak into a log line or a response by accident.
        LedgerDto.TransferResponse transfer;
        try
        {
            transfer = await ledgerApiClient.TransferAsync(
                new LedgerDto.TransferRequest(
                    request.DebitAccountId, request.CreditAccountId, request.Amount, request.Narration, request.IdempotencyKey),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // T-036: unlike LedgerApi's ReverseAsync, TransferAsync returns a normal 200/422 body with
            // Status="Failed" for a business-rule failure (see LedgerApiClient.TransferAsync's own
            // comment) - that path is handled below exactly as before and never reaches this catch.
            // This catches everything else: a 404/409/500, a timeout, or any other unexpected
            // DownstreamApiException/transport failure, after IdentityApi has already authorized the
            // transfer. Unlike ReversalService's equivalent catch-all, there is nothing to report to
            // IdentityApi here - IdentityApiClient has no RecordTransferOutcomeAsync-shaped method, and
            // IdentityApi's AuthorizationController has no outcome-reporting route for a transfer the way
            // it does for a reversal (POST .../reversal-outcome, T-020) - transfers never got the
            // deferred-grant-verification machinery reversals did, so there is no endpoint to call even
            // if we wanted to. So: just close the audit-trail gap QA found - write the same TransferPosted
            // row the success path writes, flagged Unconfirmed so an operator can tell "this definitely
            // failed" apart from "this might have posted, LedgerApi just never told us" - then rethrow the
            // original exception unchanged (do not swallow it, do not fabricate a response).
            auditLogger.Record(new AuditLog
            {
                Action = AuditActions.TransferPosted,
                EntityType = "Transfer",
                EntityId = authorization.Decision.DecisionId,
                Status = "Unconfirmed",
                Details = $"DecisionId {authorization.Decision.DecisionId}; LedgerApi failure: {ex.Message}",
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            throw;
        }

        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.TransferPosted,
            EntityType = "Transfer",
            EntityId = transfer.Reference,
            Status = transfer.Status,
            Details = $"DecisionId {authorization.Decision.DecisionId}",
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // DESIGN.md 1.5/1.12: "the transfer result plus the authorization decision id (jti) ... never the
        // grant token itself."
        return new TransferResultResponse(
            authorization.Decision.DecisionId, transfer.Reference, transfer.Status, transfer.FailureReason);
    }
}
