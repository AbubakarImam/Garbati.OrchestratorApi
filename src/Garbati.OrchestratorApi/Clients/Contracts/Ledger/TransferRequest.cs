namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Requests.TransferRequest - the ordinary single-transfer shape, not
// ChainRequest (DESIGN.md Section 1.7: no fee/tax leg in this version, so /api/transfers is right, not
// /api/chains). No grant field: LedgerApi's own request type has none yet (decision #47 is deferred).
public record TransferRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string? Narration,
    string IdempotencyKey);
