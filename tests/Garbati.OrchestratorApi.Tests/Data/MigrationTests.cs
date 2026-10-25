using Garbati.OrchestratorApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Garbati.OrchestratorApi.Tests.Data;

// Confirms the migrated database schema actually matches Entities/AccountOpening.cs - not just that EF's
// in-memory model does (task file: "Migration test for AccountOpenings' schema").
[Collection("Postgres")]
public class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AppliedMigrations_IncludeInitialCreate()
    {
        await using var ctx = fixture.CreateContext();

        var applied = await ctx.Database.GetAppliedMigrationsAsync();

        Assert.Contains(applied, m => m.EndsWith("InitialCreate"));
    }

    [Fact]
    public async Task AppliedMigrations_IncludeAddProductCodeToAccountOpenings()
    {
        await using var ctx = fixture.CreateContext();

        var applied = await ctx.Database.GetAppliedMigrationsAsync();

        Assert.Contains(applied, m => m.EndsWith("AddProductCodeToAccountOpenings"));
    }

    [Fact]
    public async Task AccountOpeningsTable_ExistsWithItsExpectedColumns()
    {
        await using var ctx = fixture.CreateContext();

        await AssertColumnsAsync(ctx, "account_openings",
            "id", "operation_id", "idempotency_key", "initiated_by", "request_hash",
            "full_name", "jurisdiction_code", "customer_reference", "email", "phone_number",
            "account_name", "account_type", "currency_code",
            "customer_id", "ledger_account_id", "product_code", "current_step", "status", "failure_reason",
            "created_at", "completed_at");

        await AssertColumnsAsync(ctx, "audit_logs",
            "id", "actor_id", "action", "entity_type", "entity_id", "details", "status",
            "correlation_id", "endpoint", "ip_address", "user_agent", "created_at");
    }

    private static async Task AssertColumnsAsync(Microsoft.EntityFrameworkCore.DbContext ctx, string table, params string[] expectedColumns)
    {
        var actual = new List<string>();
        var connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = @table";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0));
        }

        foreach (var expected in expectedColumns)
        {
            Assert.Contains(expected, actual);
        }
    }
}
