namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Requests.CreateAccountRequest.
public record CreateAccountRequest(
    string AccountName,
    string AccountType,
    string CurrencyCode,
    string IdempotencyKey);
