using Garbati.OrchestratorApi.Contracts.Requests;
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog;

namespace Garbati.OrchestratorApi.Middleware;

// Adds the request's idempotency key to the per-request log line, so retries can be traced by key
// without logging request bodies. Identical to LedgerApi's/IdentityApi's filter of the same name.
public class IdempotencyKeyLoggingFilter(IDiagnosticContext diagnosticContext) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.ActionArguments.Values.OfType<IIdempotentRequest>().FirstOrDefault();
        if (request is not null)
        {
            diagnosticContext.Set("IdempotencyKey", request.IdempotencyKey);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
