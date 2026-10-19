using Garbati.OrchestratorApi.Contracts.Requests;

namespace Garbati.OrchestratorApi.Validation;

// Mirrors TransferRequestValidator's shape: required-field/format checks only. ReasonCode is checked for
// presence here, same as every other required field - the fixed list it must belong to
// (IdentityApi.Entities.ReversalReasonCode) is IdentityApi's own business vocabulary, not duplicated here
// (a bad-but-non-empty value is rejected by IdentityApi's own validator instead, same as this service
// already leaves "is this a real account id" checks to the downstream services that own that question).
public class ReversalRequestValidator
{
    public IReadOnlyList<string> Validate(ReversalRequest request)
    {
        var errors = new List<string>();

        if (IdempotencyKeyRule.Validate(request.IdempotencyKey) is { } idempotencyError)
        {
            errors.Add(idempotencyError);
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            errors.Add("RequestedBy is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OriginalTransactionReference))
        {
            errors.Add("OriginalTransactionReference is required.");
        }

        if (request.Amount <= 0m)
        {
            errors.Add("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
        {
            errors.Add("Currency must be a 3-letter currency code.");
        }

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            errors.Add("ReasonCode is required.");
        }

        return errors;
    }
}
