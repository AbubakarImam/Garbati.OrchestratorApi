namespace Garbati.OrchestratorApi.Entities;

// DESIGN.md Section 1.4 (decision #1): durable state for the one multi-step operation this service owns.
// "Id (the operation id, returned to the caller)" from the design doc is split here into an internal
// numeric Id (the EF primary key - never exposed) and a generated public OperationId, the same split
// LedgerApi's Account/Transaction and IdentityApi's Customer/Mandate already use for every public-facing
// identifier in this platform (AccountNumber, Reference, CustomerId, MandateId) - matching the established
// convention exactly, rather than the design doc's literal field name, which would otherwise be the only
// entity in the platform using its database primary key as a caller-visible value.
//
// FullName/JurisdictionCode/.../CurrencyCode are the original request's fields, kept so a later retry can
// resume a step that was never attempted (e.g. a crash before step 1 ever ran) without the caller having
// to resend them - POST /v1/account-openings/{id}/retry takes no body (DESIGN.md Section 1.9).
public class AccountOpening
{
    public long Id { get; set; }
    public string OperationId { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;
    public string? InitiatedBy { get; set; }
    public string RequestHash { get; set; } = null!;

    public string FullName { get; set; } = null!;
    public string JurisdictionCode { get; set; } = null!;
    public string? CustomerReference { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string AccountName { get; set; } = null!;
    public string AccountType { get; set; } = null!;
    public string CurrencyCode { get; set; } = null!;

    public string? CustomerId { get; set; }
    public string? LedgerAccountId { get; set; }

    public AccountOpeningStep CurrentStep { get; set; }
    public AccountOpeningStatus Status { get; set; }
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
