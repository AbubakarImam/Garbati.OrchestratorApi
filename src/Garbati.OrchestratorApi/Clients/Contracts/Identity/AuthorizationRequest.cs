namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Requests.AuthorizationRequest.
public record AuthorizationRequest(
    string RequestedBy,
    string Operation,
    string DebitAccountId,
    string CreditAccountId,
    decimal Amount,
    string Currency,
    string IdempotencyKey);
