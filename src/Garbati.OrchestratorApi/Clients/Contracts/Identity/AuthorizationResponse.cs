namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Responses.AuthorizationResponse. Grant is read here only to be
// thrown away immediately by IdentityApiClient - this service never logs or returns it (DESIGN.md
// Section 1.7/1.12, the same "never log/return the raw grant" rule every service follows).
public record AuthorizationResponse(
    AuthorizationDecisionResponse Decision,
    string Grant,
    DateTimeOffset ExpiresAt);
