using Garbati.OrchestratorApi.Middleware;

namespace Garbati.OrchestratorApi.Clients.Implementations;

// ARCHITECTURE.md Section 5: "X-Correlation-ID on every call, passed on to every downstream call."
// CorrelationIdMiddleware establishes the id for the inbound request (as HttpContext.TraceIdentifier);
// this handler puts it on every outgoing call to IdentityApi/LedgerApi, so one operation's logs and audit
// rows line up across all three services.
public class CorrelationIdPropagationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(correlationId))
        {
            request.Headers.Remove(CorrelationIdMiddleware.HeaderName);
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
