using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.OrchestratorApi.Controllers;

// T-030. Mirrors TransfersController's own shape exactly - new operation group, new scope
// (orchestrator.reversals), same controller-per-operation-group pattern as the rest of this service.
[ApiController]
[Route("v1/reversals")]
public class ReversalsController(
    IReversalOrchestrationService reversalService,
    ReversalRequestValidator validator) : ControllerBase
{
    [Authorize(Policy = OrchestratorScopes.Reversals)]
    [HttpPost]
    public async Task<ActionResult<ReversalResultResponse>> Reverse(
        [FromBody] ReversalRequest request, CancellationToken cancellationToken)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await reversalService.ReverseAsync(request, cancellationToken);

        // Mirrors TransfersController exactly: LedgerApi having failed to recover the money (reported to
        // IdentityApi as a WriteOff outcome) is a business-rule failure, returned with 422, not a
        // fabricated 200. A denial from IdentityApi never reaches here - AuthorizationDeniedException is
        // thrown and mapped to 422 by ExceptionHandlingMiddleware before this method would ever see a
        // result.
        return response.Status == "Failed" ? UnprocessableEntity(response) : Ok(response);
    }
}
