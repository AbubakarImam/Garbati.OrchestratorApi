namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Requests.CreateCustomerRequest field-for-field (read from that
// repo's live source, not its docs, per the task file). Kept as this service's own type rather than a
// shared package: ARCHITECTURE.md P-5 - "API calls only, no shared DB" - extends naturally to "no shared
// request/response types either," so a contract change on either side is caught by a failing test here,
// not a silent compile-time coupling across repositories.
public record CreateCustomerRequest(
    string FullName,
    string JurisdictionCode,
    string? CustomerReference,
    string? Email,
    string? PhoneNumber,
    string IdempotencyKey);
