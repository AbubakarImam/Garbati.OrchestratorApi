namespace Garbati.OrchestratorApi.Contracts.Requests;

// DESIGN.md Section 1.5/1.9. RequestedBy is the caller-supplied CustomerId Section 1.6 flags as a known,
// unauthenticated gap (no real Channel exists yet) - deliberately not treated as anything stronger here.
public record TransferRequest(
    string IdempotencyKey,
    string RequestedBy,
    string DebitAccountId,
    string CreditAccountId,
    decimal Amount,
    string Currency,
    string? Narration) : IIdempotentRequest;
