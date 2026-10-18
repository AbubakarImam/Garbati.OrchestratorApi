namespace Garbati.OrchestratorApi.Services;

// Same approach as LedgerApi's TransactionReference / IdentityApi's CustomerIdGenerator: an internal,
// human-readable, generated public identifier - never a database primary key exposed directly (see
// Entities/AccountOpening.cs's comment on Id vs OperationId).
public static class OperationIdGenerator
{
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 15;
    private const string Prefix = "OPN";

    public static string Generate()
    {
        var id = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            id[i] = AllowedChars[Random.Shared.Next(AllowedChars.Length)];
        }
        return Prefix + "-" + new string(id);
    }
}
