using System.Text.RegularExpressions;

namespace Garbati.OrchestratorApi.Validation;

// Same rule as LedgerApi's/IdentityApi's IdempotencyKeyRule: short enough to always fit a unique index,
// and limited to characters that are safe in logs and URLs. The reserved prefix here is "orch:" instead
// of "chain:" - DESIGN.md Section 1.4 reserves that namespace for this service's own derived downstream
// keys (orch:{operationId}:create-customer etc.), so a caller cannot forge one.
public static partial class IdempotencyKeyRule
{
    public const int MaxLength = 128;

    public const string ReservedPrefix = "orch:";

    // \z, not $: in .NET, $ also matches just before a trailing newline, so "abc\n" would pass.
    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,128}\z")]
    private static partial Regex Pattern();

    public static string? Validate(string? key) =>
        string.IsNullOrWhiteSpace(key) ? "IdempotencyKey is required."
        : !Pattern().IsMatch(key) ? $"IdempotencyKey must be 1-{MaxLength} characters: letters, digits, '-', '_', '.' or ':'."
        : key.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase) ? $"IdempotencyKey must not start with '{ReservedPrefix}': it is reserved for this service's own derived downstream keys."
        : null;
}
