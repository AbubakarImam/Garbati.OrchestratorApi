using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Validation;

namespace Garbati.OrchestratorApi.Tests.Validation;

// Mirrors TransferRequestValidatorTests's shape.
public class ReversalRequestValidatorTests
{
    private readonly ReversalRequestValidator _validator = new();

    private static ReversalRequest ValidRequest() => new(
        "a-valid-key", "CUS-TEST", "TXN-ORIGINAL1", 500m, "NGN", "CUST");

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
    public void Validate_RequiresOriginalTransactionReference() =>
        Assert.Contains("OriginalTransactionReference is required.", _validator.Validate(ValidRequest() with { OriginalTransactionReference = " " }));

    [Fact]
    public void Validate_RequiresReasonCode() =>
        Assert.Contains("ReasonCode is required.", _validator.Validate(ValidRequest() with { ReasonCode = "" }));

    [Theory]
    [InlineData("NG")]
    [InlineData("NGNX")]
    [InlineData("")]
    public void Validate_RequiresAThreeLetterCurrencyCode(string currency) =>
        Assert.Contains("Currency must be a 3-letter currency code.", _validator.Validate(ValidRequest() with { Currency = currency }));
}
