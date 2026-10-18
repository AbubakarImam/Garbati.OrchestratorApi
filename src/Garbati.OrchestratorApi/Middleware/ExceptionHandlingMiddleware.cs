using Garbati.OrchestratorApi.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.OrchestratorApi.Middleware;

// Same shape as LedgerApi's/IdentityApi's middleware of the same name (ARCHITECTURE.md Section 5's
// status-code mapping: 404 not found, 409 conflict, 422 business-rule failure, 500 unexpected; 400/401/403
// are handled per-controller/by ApiKeyAuthenticationHandler, not here).
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request cancelled by client: {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, title) = Map(ex);
            var isExpected = status != StatusCodes.Status500InternalServerError;

            if (isExpected)
                logger.LogWarning(ex, "Request rejected ({Status}) {Method} {Path}", status, context.Request.Method, context.Request.Path);
            else
                logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = status;

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                // Only expected business errors expose their message; unexpected ones must not leak internals.
                Detail = isExpected ? ex.Message : null,
                Extensions = { ["correlationId"] = context.TraceIdentifier }
            };

            // DESIGN.md Section 1.5/1.11/1.12: a transfer denial carries the decision id and a stable
            // reason code - never the grant, which was never issued on this path. Same shape IdentityApi's
            // own ExceptionHandlingMiddleware uses for its AuthorizationDeniedException.
            if (ex is AuthorizationDeniedException authorizationDenied)
            {
                problem.Extensions["decisionId"] = authorizationDenied.DecisionId;
                problem.Extensions["reasonCode"] = authorizationDenied.ReasonCode;
            }

            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        }
    }

    private static (int Status, string Title) Map(Exception ex) => ex switch
    {
        AccountOpeningNotFoundException => (StatusCodes.Status404NotFound, "Account opening not found."),
        DuplicateIdempotencyKeyException => (StatusCodes.Status409Conflict, "Duplicate idempotency key."),
        MandateAlreadyAttachedException => (StatusCodes.Status409Conflict, "Mandate already attached."),
        AuthorizationDeniedException => (StatusCodes.Status422UnprocessableEntity, "Authorization denied."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };
}
