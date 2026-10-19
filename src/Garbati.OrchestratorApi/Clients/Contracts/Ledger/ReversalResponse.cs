namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Responses.TransactionResponse - this is what LedgerApi's own
// ReversalsController actually returns (ActionResult<TransactionResponse>, not LedgerApi's own
// TransferResponse), confirmed against its live source. Deliberately no FailureReason field: unlike a
// transfer, LedgerApi's reversal failure path (insufficient funds, already reversed, not found, ...)
// always throws before any response body is built (its own decision #27: "no failed row is kept") - so
// there is no Status="Failed" body variant to mirror here. A thrown exception (surfaced by
// ILedgerApiClient.ReverseAsync as DownstreamApiException) is this client's only failure signal.
public record ReversalResponse(
    string Reference,
    string Status,
    string? Narration,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    // Set only for a chain leg (decision #45) - never posted here, kept only so a response with these
    // fields still deserializes without error.
    string? ChainReference = null,
    int? Sequence = null,
    string? Purpose = null);
