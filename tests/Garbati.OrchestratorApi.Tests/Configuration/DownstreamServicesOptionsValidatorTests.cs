using Garbati.OrchestratorApi.Configuration;

namespace Garbati.OrchestratorApi.Tests.Configuration;

// T-046 task file's test list: "DownstreamServicesOptionsValidator rejects a missing/invalid ProductApi
// entry, mirroring the existing tests for IdentityApi/LedgerApi." Runs at startup (ValidateOnStart,
// Program.cs) so a missing base URL or key surfaces immediately rather than deep inside account opening.
public class DownstreamServicesOptionsValidatorTests
{
    private static DownstreamServicesOptions Valid() => new()
    {
        IdentityApi = new DownstreamServiceOptions { BaseUrl = "http://identity.invalid", ApiKey = "key" },
        LedgerApi = new DownstreamServiceOptions { BaseUrl = "http://ledger.invalid", ApiKey = "key" },
        ProductApi = new DownstreamServiceOptions { BaseUrl = "http://product.invalid", ApiKey = "key" },
    };

    [Fact]
    public void Validate_WithAllThreeServicesConfigured_Succeeds()
    {
        var result = new DownstreamServicesOptionsValidator().Validate(null, Valid());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void Validate_WhenProductApiBaseUrlIsMissingOrInvalid_Fails(string baseUrl)
    {
        var options = Valid();
        options.ProductApi.BaseUrl = baseUrl;

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:ProductApi:BaseUrl"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenProductApiApiKeyIsMissing_Fails(string apiKey)
    {
        var options = Valid();
        options.ProductApi.ApiKey = apiKey;

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:ProductApi:ApiKey"));
    }

    // Mirrors the ProductApi cases above - IdentityApi/LedgerApi are checked by the exact same Check(...)
    // helper, so this proves the pre-existing two entries are still validated correctly alongside the
    // new third one, not just that adding ProductApi didn't break them by inspection.
    [Fact]
    public void Validate_WhenIdentityApiIsMissing_Fails()
    {
        var options = Valid();
        options.IdentityApi.BaseUrl = "";
        options.IdentityApi.ApiKey = "";

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:IdentityApi:BaseUrl"));
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:IdentityApi:ApiKey"));
    }

    [Fact]
    public void Validate_WhenLedgerApiIsMissing_Fails()
    {
        var options = Valid();
        options.LedgerApi.BaseUrl = "";
        options.LedgerApi.ApiKey = "";

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:LedgerApi:BaseUrl"));
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:LedgerApi:ApiKey"));
    }
}
