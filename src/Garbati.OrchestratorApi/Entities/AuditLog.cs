namespace Garbati.OrchestratorApi.Entities;

// DESIGN.md Section 1.12: "One audit row per operation-level event ... Includes the correlation id and,
// for a transfer, the authorization decision id (jti) - never the grant token itself." Shape mirrors
// LedgerApi's AuditLog / IdentityApi's AuditEvent, generalized the same way IdentityApi's is: Details
// carries whatever business context an action needs instead of ledger-specific columns.
public class AuditLog
{
    public long Id { get; set; }
    public string? ActorId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string? Details { get; set; }
    public string? Status { get; set; }
    public string? CorrelationId { get; set; }
    public string? Endpoint { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
