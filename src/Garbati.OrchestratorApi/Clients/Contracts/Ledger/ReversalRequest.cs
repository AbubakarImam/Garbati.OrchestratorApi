namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Requests.ReversalRequest - its existing ReversalsController
// (LedgerScopes.Reverse policy), already built, not new (task file T-030).
public record ReversalRequest(string OriginalTransactionReference, string? Reason);
