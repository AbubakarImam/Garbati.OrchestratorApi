namespace Garbati.OrchestratorApi.Exceptions;

// Thrown by AccountOpeningService.DetermineProductCode when StartAccountOpeningRequest.AccountType is
// anything other than the two values every real caller in this codebase actually sends today ("Savings"/
// "Current" - AccountType is a free-text field, not a fixed enum anywhere in this codebase). Per the task
// file: "do not guess" at a product mapping for an unrecognised AccountType - this is caught by
// RunFromCurrentStepAsync's own existing outer catch (Exception ex) block exactly like every other step
// failure, surfacing as Status = Failed, FailureReason = ex.Message - not a crash, not a silent success.
public class NoProductMappingException(string message) : Exception(message);
