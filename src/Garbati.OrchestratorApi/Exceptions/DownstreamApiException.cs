namespace Garbati.OrchestratorApi.Exceptions;

// Thrown by the IdentityApi/LedgerApi HTTP clients for any response the Orchestrator does not know how
// to treat as a specific, named outcome (AuthorizationDeniedException and MandateAlreadyAttachedException
// cover the two cases that do get special handling). Carries enough to explain an account-opening
// FailureReason without repeating the downstream service's raw response body (which could contain
// customer-identifying details ARCHITECTURE.md Section 5 says must never be logged).
public class DownstreamApiException(string serviceName, int statusCode, string detail)
    : Exception($"{serviceName} returned {statusCode}: {detail}")
{
    public string ServiceName { get; } = serviceName;
    public int StatusCode { get; } = statusCode;
}
