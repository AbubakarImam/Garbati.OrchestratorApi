namespace Garbati.OrchestratorApi.Contracts.Responses;

// DESIGN.md Section 1.4/1.9: "the operation, including customerId and ledgerAccountId once complete" /
// "current state - which step, and customerId/ledgerAccountId if set."
//
// T-046: ProductCode added as the last field - null until the new BindProduct step runs, same as
// CustomerId/LedgerAccountId are null before their own steps run - so a caller can see what the account
// was actually bound to once the operation completes.
public record AccountOpeningResponse(
    string Id,
    string Status,
    string CurrentStep,
    string? CustomerId,
    string? LedgerAccountId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ProductCode);
