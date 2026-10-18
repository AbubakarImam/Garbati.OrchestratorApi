namespace Garbati.OrchestratorApi.Contracts.Responses;

// DESIGN.md Section 1.4/1.9: "the operation, including customerId and ledgerAccountId once complete" /
// "current state - which step, and customerId/ledgerAccountId if set."
public record AccountOpeningResponse(
    string Id,
    string Status,
    string CurrentStep,
    string? CustomerId,
    string? LedgerAccountId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
