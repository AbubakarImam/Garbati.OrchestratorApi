using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.OrchestratorApi.Controllers;

// DESIGN.md Section 1.9.
[ApiController]
[Route("v1/transfers")]
public class TransfersController(
    ITransferOrchestrationService transferService,
    TransferRequestValidator validator) : ControllerBase
{
    [Authorize(Policy = OrchestratorScopes.Transfers)]
    [HttpPost]
    public async Task<ActionResult<TransferResultResponse>> Transfer(
        [FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await transferService.TransferAsync(request, cancellationToken);

        // Mirrors LedgerApi's own TransfersController: LedgerApi having posted the leg as Failed is a
        // business-rule failure, returned with 422, not a fabricated 200 (ARCHITECTURE.md Section 5).
        // A denial from IdentityApi never reaches here - AuthorizationDeniedException is thrown and
        // mapped to 422 by ExceptionHandlingMiddleware before this method would ever see a result.
        return response.Status == "Failed" ? UnprocessableEntity(response) : Ok(response);
    }
}
