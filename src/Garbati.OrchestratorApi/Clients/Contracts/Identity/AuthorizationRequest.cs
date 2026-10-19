namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Requests.AuthorizationRequest. T-030: CreditAccountId is now
// nullable and ReasonCode is added, catching this mirror up with IdentityApi's own T-020 changes to the
// real contract (confirmed against its live source) - CreditAccountId has no meaning for a Reversal (no
// destination account to name) and DebitAccountId instead carries the original transaction reference for
// that operation (IdentityApi's own documented convention, see ReversalService's comment). Both additions
// are additive/widening for TransferService's existing call (non-null CreditAccountId, ReasonCode
// defaulted to null), so its behavior is unchanged.
public record AuthorizationRequest(
    string RequestedBy,
    string Operation,
    string DebitAccountId,
    string? CreditAccountId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string? ReasonCode = null);
