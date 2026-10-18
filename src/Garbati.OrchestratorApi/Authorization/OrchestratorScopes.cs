namespace Garbati.OrchestratorApi.Authorization;

// Each scope is also the name of the authorization policy that requires it (same pattern as
// LedgerApi.Authorization.LedgerScopes / Garbati.IdentityApi.Authorization.IdentityScopes).
//
// This is the first caller-facing surface this service has (DESIGN.md Section 1.9/1.10 only lists the
// scopes the Orchestrator presents *to* IdentityApi/LedgerApi as a client - not what it requires of its
// own callers), so there is no existing convention to mirror for this choice (task file: "use your
// judgment and say what you chose in the PR"). One scope per operation group - account opening vs.
// transfer - rather than a single scope covering both: a demo driver wiring up only transfers (say,
// against accounts opened some other way) should not also need account-opening rights, and vice versa.
// See the PR description for the full reasoning.
public static class OrchestratorScopes
{
    public const string ClaimType = "scope";

    public const string AccountOpenings = "orchestrator.account-openings";
    public const string Transfers = "orchestrator.transfers";

    public static readonly IReadOnlyList<string> All = [AccountOpenings, Transfers];
}
