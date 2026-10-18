using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Entities;

namespace Garbati.OrchestratorApi.Auditing;

public class AuditLogger(OrchestratorDbContext dbContext, IHttpContextAccessor httpContextAccessor) : IAuditLogger
{
    private const int MaxUserAgentLength = 512;

    public AuditLog Record(AuditLog entry)
    {
        // Request details are filled in here so callers only describe the business change. Outside an
        // HTTP request (e.g. tests) they stay null.
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            entry.ActorId ??= httpContext.User.Identity?.Name;
            entry.CorrelationId = httpContext.TraceIdentifier;
            entry.Endpoint = $"{httpContext.Request.Method} {httpContext.Request.Path}";
            entry.IpAddress = httpContext.Connection.RemoteIpAddress?.ToString();

            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            entry.UserAgent = userAgent.Length switch
            {
                0 => null,
                > MaxUserAgentLength => userAgent[..MaxUserAgentLength],
                _ => userAgent
            };
        }

        entry.CreatedAt = DateTimeOffset.UtcNow;
        dbContext.AuditLogs.Add(entry);
        return entry;
    }
}
