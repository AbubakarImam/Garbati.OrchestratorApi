namespace Garbati.OrchestratorApi.Exceptions;

// Thrown by IdentityApiClient.RequestAuthorizationAsync when IdentityApi's POST /v1/authorization/requests
// responds 422 (Garbati.IdentityApi.Exceptions.AuthorizationDeniedException on its side). DecisionId and
// ReasonCode are read back from the ProblemDetails extensions IdentityApi's own ExceptionHandlingMiddleware
// adds for that exception - never the grant, which was never issued on a denial.
//
// DESIGN.md 1.11: "IdentityApi denies authorization for a transfer -> Stop; do not call LedgerApi. Return
// the denial reason, not a fabricated success." TransfersController maps this to 422, mirroring LedgerApi's
// and IdentityApi's own 422-for-business-rule-failure convention.
public class AuthorizationDeniedException(string? decisionId, string? reasonCode, string message) : Exception(message)
{
    public string? DecisionId { get; } = decisionId;
    public string? ReasonCode { get; } = reasonCode;
}
