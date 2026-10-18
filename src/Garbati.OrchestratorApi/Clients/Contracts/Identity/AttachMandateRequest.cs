namespace Garbati.OrchestratorApi.Clients.Contracts.Identity;

// Mirrors Garbati.IdentityApi.Contracts.Requests.AttachMandateRequest - deliberately has NO
// IdempotencyKey field, confirmed against the live source rather than assumed (task file: "check whether
// AttachMandateRequest actually needs/accepts one from the caller"). See
// Exceptions/MandateAlreadyAttachedException.cs for what this means for retry safety on this one step.
public record AttachMandateRequest(string LedgerAccountId, List<string> Operations);
