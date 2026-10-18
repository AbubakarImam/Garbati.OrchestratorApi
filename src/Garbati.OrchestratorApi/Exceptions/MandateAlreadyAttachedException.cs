namespace Garbati.OrchestratorApi.Exceptions;

// Thrown by IdentityApiClient.AttachMandateAsync when IdentityApi responds 409 to POST
// /v1/customers/{customerId}/mandates (Garbati.IdentityApi.Exceptions.DuplicateActiveMandateException on
// its side - "customer already has an active mandate on this account").
//
// Unlike create-customer and create-account, AttachMandateRequest has no IdempotencyKey field at all
// (confirmed against the live source, not assumed - see the PR description), so a retry of this specific
// step cannot rely on a derived idempotency key the way steps 1 and 3 do. AccountOpeningService's retry
// path instead treats this exact 409 as proof the attach already succeeded on an earlier attempt (the
// LedgerAccountId in the 409 is the one this same operation itself created and stored in step 3) and
// continues to Complete rather than surfacing it as a new failure.
public class MandateAlreadyAttachedException(string message) : Exception(message);
