namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Responses.AuthorizationDecisionResponse. Deliberately has no
// Grant field - IdentityApi's own type of the same name never carries the grant either.
public record AuthorizationDecisionResponse(
    string DecisionId,
    bool Approved,
    string? DenialReason,
    string RequestedBy,
    string Operation,
    string DebitAccountId,
    string? CreditAccountId,
    string Amount,
    string Currency,
    string? MandateId,
    DateTimeOffset CreatedAt);
