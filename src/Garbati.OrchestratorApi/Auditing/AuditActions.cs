namespace Garbati.OrchestratorApi.Auditing;

// The state changes that are audited (DESIGN.md Section 1.12). Outcome (success/failed/denied) is in
// AuditLog.Status, not the action name.
public static class AuditActions
{
    public const string AccountOpeningStarted = "AccountOpeningStarted";
    public const string AccountOpeningStepSucceeded = "AccountOpeningStepSucceeded";
    public const string AccountOpeningStepFailed = "AccountOpeningStepFailed";
    public const string AccountOpeningCompleted = "AccountOpeningCompleted";

    public const string TransferAuthorized = "TransferAuthorized";
    public const string TransferDenied = "TransferDenied";
    public const string TransferPosted = "TransferPosted";

    public const string ReversalAuthorized = "ReversalAuthorized";
    public const string ReversalDenied = "ReversalDenied";
    public const string ReversalPosted = "ReversalPosted";

    // T-030 QA fix: on the "genuinely unknown LedgerApi outcome" path, the outcome-report call to
    // IdentityApi (POST .../reversal-outcome) is itself wrapped in a try/catch, so a failure reporting
    // the outcome can never mask the original LedgerApi failure by throwing unhandled. This is that
    // failure, audited under its own name so an operator can find it distinctly from a normal
    // ReversalPosted row.
    public const string ReversalOutcomeReportFailed = "ReversalOutcomeReportFailed";
}
