using Garbati.OrchestratorApi.Validation;

namespace Garbati.OrchestratorApi.Tests.Validation;

public class IdempotencyKeyRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RequiresAKey(string? key) =>
        Assert.Equal("IdempotencyKey is required.", IdempotencyKeyRule.Validate(key));

    [Theory]
    [InlineData("has a space")]
    [InlineData("has/a/slash")]
    public void Validate_RejectsDisallowedCharacters(string key) =>
        Assert.NotNull(IdempotencyKeyRule.Validate(key));

    [Fact]
    public void Validate_RejectsTheReservedOrchPrefix()
    {
        var error = IdempotencyKeyRule.Validate("orch:not-a-real-operation:create-customer");

        Assert.Contains("reserved", error);
    }

    [Theory]
    [InlineData("a-valid-key")]
    [InlineData("ABC123._-:")]
    public void Validate_AcceptsOrdinaryKeys(string key) =>
        Assert.Null(IdempotencyKeyRule.Validate(key));
}
