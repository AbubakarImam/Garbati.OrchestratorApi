using Garbati.OrchestratorApi.Entities;

namespace Garbati.OrchestratorApi.Auditing;

public interface IAuditLogger
{
    // Stages an audit row on the current DbContext; it is written by the caller's next SaveChanges, so it
    // commits (or rolls back) together with the change it describes (ARCHITECTURE.md Section 5: "written
    // in the same database transaction as the change it describes").
    AuditLog Record(AuditLog entry);
}
