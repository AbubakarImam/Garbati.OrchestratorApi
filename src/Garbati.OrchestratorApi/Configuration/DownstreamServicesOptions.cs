using Microsoft.Extensions.Options;

namespace Garbati.OrchestratorApi.Configuration;

// This service's own outbound credentials: the X-Api-Key value it presents TO IdentityApi/LedgerApi, and
// the base URL it calls them at. Deliberately separate from Authorization/ApiKeyOptions, which holds only
// hashes of the keys OTHER clients present TO this service - an outbound key must be a usable secret, not
// a one-way hash, so it cannot live in the same options type or configuration section.
public class DownstreamServiceOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class DownstreamServicesOptions
{
    public DownstreamServiceOptions IdentityApi { get; set; } = new();
    public DownstreamServiceOptions LedgerApi { get; set; } = new();

    // T-046: this service as a client of Garbati.ProductApi (the new BindProduct account-opening step).
    public DownstreamServiceOptions ProductApi { get; set; } = new();
}

// Runs at startup (ValidateOnStart): a missing base URL or key would otherwise surface only the first
// time a request tries to use it, as a confusing failure deep inside account opening or a transfer.
public class DownstreamServicesOptionsValidator : IValidateOptions<DownstreamServicesOptions>
{
    public ValidateOptionsResult Validate(string? name, DownstreamServicesOptions options)
    {
        var errors = new List<string>();

        Check(options.IdentityApi, "DownstreamServices:IdentityApi", errors);
        Check(options.LedgerApi, "DownstreamServices:LedgerApi", errors);
        Check(options.ProductApi, "DownstreamServices:ProductApi", errors);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Check(DownstreamServiceOptions service, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(service.BaseUrl) || !Uri.TryCreate(service.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add($"{prefix}:BaseUrl must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(service.ApiKey))
        {
            errors.Add($"{prefix}:ApiKey is required.");
        }
    }
}
