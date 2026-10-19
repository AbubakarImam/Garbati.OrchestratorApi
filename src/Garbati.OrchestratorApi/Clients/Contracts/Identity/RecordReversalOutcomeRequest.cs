namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Requests.RecordReversalOutcomeRequest. Outcome must be
// "Completed" or "WriteOff" (IdentityApi's own ReversalOutcomeKind, a deliberately two-state enum,
// decision #34) - ReversalService sends the literal string, same as TransferService already sends
// literal Operation/status strings elsewhere in this codebase rather than mirroring every sibling enum.
public record RecordReversalOutcomeRequest(string Outcome, string RecordedBy, string? Notes);
