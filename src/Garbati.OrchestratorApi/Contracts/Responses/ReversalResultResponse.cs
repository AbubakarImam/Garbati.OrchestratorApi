namespace Garbati.OrchestratorApi.Contracts.Responses;

// Mirrors TransferResultResponse's own shape and comment: the reversal result plus the authorization
// decision id (jti) - never the grant token itself. Status/Reference follow the same Success/Failed
// vocabulary TransferResultResponse/LedgerApi's TransferResponse already use (see ReversalService's own
// comment for why LedgerApi's reversal failure path - which throws rather than returning a body - still
// maps onto that same vocabulary here). DecisionId is IdentityApi's AuthorizationDecisionResponse.DecisionId.
public record ReversalResultResponse(
    string DecisionId,
    string Reference,
    string Status,
    string? FailureReason);
