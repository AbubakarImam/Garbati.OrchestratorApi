namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Requests.KycDecisionRequest.
public record KycDecisionRequest(
    string KycStatus,
    string? KycTier,
    string VerificationType,
    string Provider,
    string? ProviderReference,
    string? ResultCode,
    DateTimeOffset? ExpiresAt);
