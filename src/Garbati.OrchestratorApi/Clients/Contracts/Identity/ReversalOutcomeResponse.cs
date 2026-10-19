namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Responses.ReversalOutcomeResponse.
public record ReversalOutcomeResponse(
    string AuthorizationDecisionId,
    string Outcome,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    string? Notes);
