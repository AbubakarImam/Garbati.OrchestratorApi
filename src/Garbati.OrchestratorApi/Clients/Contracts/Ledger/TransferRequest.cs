namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Requests.TransferRequest - the ordinary single-transfer shape, not
// ChainRequest (DESIGN.md Section 1.7: no fee/tax leg in this version, so /api/transfers is right, not
// /api/chains). No grant field: LedgerApi's own request type has none yet (decision #47 is deferred).
// DecisionId (DESIGN.md decision #11, T-065, LedgerApi decision #52/T-064): the one field from LedgerApi's
// own ValueDate/BranchCode/Channel/DecisionId quartet (its decisions #49-#52) mirrored here - the only one
// with a real, already-available value today (authorization.Decision.DecisionId, received from IdentityApi
// on every call). ValueDate/BranchCode/Channel are deliberately NOT added: they have no real caller yet by
// design, and this task does not become that caller (decision #11's own scope note).
public record TransferRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string? Narration,
    string IdempotencyKey,
    string? DecisionId = null);
