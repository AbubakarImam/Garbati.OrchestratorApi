namespace Garbati.OrchestratorApi.Entities;

// DESIGN.md Section 1.3/1.4. Order matters: AccountOpeningService.RunFromAsync switches on this to decide
// where to resume.
public enum AccountOpeningStep
{
    CreateCustomer,
    VerifyKyc,
    CreateLedgerAccount,
    AttachMandate,
    Complete,
}
