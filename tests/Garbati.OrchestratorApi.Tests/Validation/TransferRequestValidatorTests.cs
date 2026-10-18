using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Validation;

namespace Garbati.OrchestratorApi.Tests.Validation;

public class TransferRequestValidatorTests
{
    private readonly TransferRequestValidator _validator = new();

    private static TransferRequest ValidRequest() => new(
        "a-valid-key", "CUS-TEST", "1000000001", "1000000002", 500m, "NGN", "narration");

    [Fact]
    public void Validate_AcceptsAFullyPopulatedRequest() =>
        Assert.Empty(_validator.Validate(ValidRequest()));

    [Fact]
    public void Validate_RejectsZeroOrNegativeAmount()
    {
        Assert.Contains("Amount must be greater than zero.", _validator.Validate(ValidRequest() with { Amount = 0m }));
        Assert.Contains("Amount must be greater than zero.", _validator.Validate(ValidRequest() with { Amount = -5m }));
    }

    [Fact]
    public void Validate_RejectsTheSameDebitAndCreditAccount()
    {
        var request = ValidRequest() with { CreditAccountId = "1000000001" };

        Assert.Contains("Same accounts must be different.", _validator.Validate(request));
    }

    [Theory]
    [InlineData("NG")]
    [InlineData("NGNX")]
    [InlineData("")]
    public void Validate_RequiresAThreeLetterCurrencyCode(string currency) =>
        Assert.Contains("Currency must be a 3-letter currency code.", _validator.Validate(ValidRequest() with { Currency = currency }));
}
