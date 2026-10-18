namespace Garbati.OrchestratorApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Responses.AccountResponse.
public record AccountResponse(
    string AccountNumber,
    string AccountName,
    string AccountType,
    string AccountClass,
    string CurrencyCode,
    string Status,
    DateTimeOffset CreatedAt);
