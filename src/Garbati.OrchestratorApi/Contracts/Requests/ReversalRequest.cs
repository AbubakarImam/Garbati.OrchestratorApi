namespace Garbati.OrchestratorApi.Contracts.Requests;

// T-030/DESIGN.md Section 1.5-shaped sibling for reversal. RequestedBy is the same caller-supplied,
// unauthenticated-behind-it CustomerId TransferRequest already uses (see TransferRequest's own comment) -
// not a new gap introduced here. Amount/Currency are caller-supplied, not looked up: LedgerApi has no
// lookup-by-reference endpoint to derive them from (its TransactionResponse carries no amount at all,
// confirmed against its live source before this task was written) - the same "caller supplies it" pattern
// TransferRequest already uses, just applied to a new operation, not a new pattern.
public record ReversalRequest(
    string IdempotencyKey,
    string RequestedBy,
    string OriginalTransactionReference,
    decimal Amount,
    string Currency,
    string ReasonCode) : IIdempotentRequest;
