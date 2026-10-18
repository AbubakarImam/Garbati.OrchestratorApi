using Garbati.OrchestratorApi.Contracts.Requests;

namespace Garbati.OrchestratorApi.Validation;

public class StartAccountOpeningRequestValidator
{
    public IReadOnlyList<string> Validate(StartAccountOpeningRequest request)
    {
        var errors = new List<string>();

        if (IdempotencyKeyRule.Validate(request.IdempotencyKey) is { } idempotencyError)
        {
            errors.Add(idempotencyError);
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors.Add("FullName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.JurisdictionCode))
        {
            errors.Add("JurisdictionCode is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AccountName))
        {
            errors.Add("AccountName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AccountType))
        {
            errors.Add("AccountType is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CurrencyCode))
        {
            errors.Add("CurrencyCode is required.");
        }

        return errors;
    }
}
