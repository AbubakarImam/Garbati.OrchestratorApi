using Garbati.OrchestratorApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace Garbati.OrchestratorApi.Data;

public class OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options) : DbContext(options)
{
    public DbSet<AccountOpening> AccountOpenings => Set<AccountOpening>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountOpening>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.OperationId).IsUnique();

            // Keys are scoped to the calling client, same scheme as LedgerApi decision #42/#43 and
            // IdentityApi decision #29 (ARCHITECTURE.md Section 5: "unique per calling client"). Nulls
            // are not distinct so two keyless rows could never accidentally be treated as a match, though
            // every row created by this service always has a key.
            entity.HasIndex(a => new { a.InitiatedBy, a.IdempotencyKey }).IsUnique().AreNullsDistinct(false);

            entity.Property(a => a.CurrentStep).HasConversion<string>();
            entity.Property(a => a.Status).HasConversion<string>();
            entity.ToTable(t => t.HasCheckConstraint("ck_account_openings_current_step",
                "\"current_step\" IN ('CreateCustomer', 'VerifyKyc', 'CreateLedgerAccount', 'AttachMandate', 'BindProduct', 'Complete')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_account_openings_status",
                "\"status\" IN ('InProgress', 'Complete', 'Failed')"));
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.EntityId);
            entity.HasIndex(a => a.CorrelationId);
        });
    }
}
