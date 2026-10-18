using Garbati.OrchestratorApi.Contracts.Requests;

namespace Garbati.OrchestratorApi.Validation;

public class TransferRequestValidator
{
    public IReadOnlyList<string> Validate(TransferRequest request)
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

        if (string.IsNullOrWhiteSpace(request.DebitAccountId))
        {
            errors.Add("DebitAccountId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CreditAccountId))
        {
            errors.Add("CreditAccountId is required.");
        }

        if (request.DebitAccountId == request.CreditAccountId)
        {
            errors.Add("Same accounts must be different.");
        }

        if (request.Amount <= 0m)
        {
            errors.Add("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
        {
            errors.Add("Currency must be a 3-letter currency code.");
        }

        return errors;
    }
}
