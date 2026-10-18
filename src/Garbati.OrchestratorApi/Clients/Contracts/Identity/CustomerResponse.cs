namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Responses.CustomerResponse.
public record CustomerResponse(
    string CustomerId,
    string? CustomerReference,
    string CustomerStatus,
    string KycStatus,
    string? KycTier,
    string JurisdictionCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
