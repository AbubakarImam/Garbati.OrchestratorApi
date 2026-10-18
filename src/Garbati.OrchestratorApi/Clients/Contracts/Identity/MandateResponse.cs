namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Responses.MandateResponse.
public record MandateResponse(
    string MandateId,
    string CustomerId,
    string LedgerAccountId,
    string Status,
    IReadOnlyList<string> Operations,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? RevokedAt,
    string? RevokedBy,
    string? RevocationReason);
