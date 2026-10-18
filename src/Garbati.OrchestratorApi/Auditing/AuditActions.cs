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
}
