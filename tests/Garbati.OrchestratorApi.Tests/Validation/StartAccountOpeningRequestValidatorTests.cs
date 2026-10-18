using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Validation;

namespace Garbati.OrchestratorApi.Tests.Validation;

public class StartAccountOpeningRequestValidatorTests
{
    private readonly StartAccountOpeningRequestValidator _validator = new();

    private static StartAccountOpeningRequest ValidRequest() => new(
        "a-valid-key", "Ada Lovelace", "NG", null, "ada@example.com", "+2348000000000",
        "Ada Savings", "Savings", "NGN");

    [Fact]
    public void Validate_AcceptsAFullyPopulatedRequest() =>
        Assert.Empty(_validator.Validate(ValidRequest()));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_RequiresFullName(string? fullName) =>
        Assert.Contains("FullName is required.", _validator.Validate(ValidRequest() with { FullName = fullName! }));

    [Fact]
    public void Validate_RequiresAccountName() =>
        Assert.Contains("AccountName is required.", _validator.Validate(ValidRequest() with { AccountName = "" }));

    [Fact]
    public void Validate_RejectsAMissingIdempotencyKey() =>
        Assert.Contains("IdempotencyKey is required.", _validator.Validate(ValidRequest() with { IdempotencyKey = "" }));
}
