namespace Garbati.OrchestratorApi.Contracts.Responses;

// DESIGN.md Section 1.5/1.9/1.12: "the transfer result plus the authorization decision id (jti) ... never
// the grant token itself." Reference/Status/FailureReason come straight from LedgerApi's TransactionResponse
// for this leg; DecisionId is IdentityApi's AuthorizationDecisionResponse.DecisionId.
public record TransferResultResponse(
    string DecisionId,
    string Reference,
    string Status,
    string? FailureReason);
