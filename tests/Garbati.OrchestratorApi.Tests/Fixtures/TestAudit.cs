using Garbati.OrchestratorApi.Auditing;
using Garbati.OrchestratorApi.Data;
using Microsoft.AspNetCore.Http;

namespace Garbati.OrchestratorApi.Tests.Fixtures;

// Service tests run outside an HTTP request, so request details on audit rows stay null.
public static class TestAudit
{
    public static IAuditLogger For(OrchestratorDbContext ctx) => new AuditLogger(ctx, new HttpContextAccessor());
}
