namespace Garbati.OrchestratorApi.Contracts.Requests;

// DESIGN.md Section 1.3/1.4/1.9, task file Section 1: the customer-creation fields IdentityApi's own
// CreateCustomerRequest needs, plus the new account's fields for LedgerApi's CreateAccountRequest, plus
// this operation's own IdempotencyKey (decision #2 - a single key for the whole multi-step operation,
// distinct from the derived per-downstream-call keys this service generates internally).
public record StartAccountOpeningRequest(
    string IdempotencyKey,
    string FullName,
    string JurisdictionCode,
    string? CustomerReference,
    string? Email,
    string? PhoneNumber,
    string AccountName,
    string AccountType,
    string CurrencyCode) : IIdempotentRequest;
