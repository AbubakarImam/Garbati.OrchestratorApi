using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;
using Garbati.OrchestratorApi.Entities;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.OrchestratorApi.Controllers;

// DESIGN.md Section 1.9. Route literals (not [controller]) match the versioned paths the design doc
// spells out exactly (ARCHITECTURE.md Section 5: "/v1/ in the path").
[ApiController]
[Route("v1/account-openings")]
public class AccountOpeningsController(
    IAccountOpeningService accountOpeningService,
    StartAccountOpeningRequestValidator validator) : ControllerBase
{
    [Authorize(Policy = OrchestratorScopes.AccountOpenings)]
    [HttpPost]
    public async Task<ActionResult<AccountOpeningResponse>> StartAccountOpening(
        [FromBody] StartAccountOpeningRequest request, CancellationToken cancellationToken)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await accountOpeningService.StartAsync(request, cancellationToken);

        // Mirrors LedgerApi's TransfersController: a business-rule failure is committed and returned like
        // a success, but with 422 (ARCHITECTURE.md Section 5: "422 business-rule failure").
        return response.Status == nameof(AccountOpeningStatus.Failed)
            ? UnprocessableEntity(response)
            : Ok(response);
    }

    [Authorize(Policy = OrchestratorScopes.AccountOpenings)]
    [HttpGet("{id}")]
    public async Task<ActionResult<AccountOpeningResponse>> GetAccountOpening(string id, CancellationToken cancellationToken)
    {
        var response = await accountOpeningService.GetAsync(id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [Authorize(Policy = OrchestratorScopes.AccountOpenings)]
    [HttpPost("{id}/retry")]
    public async Task<ActionResult<AccountOpeningResponse>> RetryAccountOpening(string id, CancellationToken cancellationToken)
    {
        var response = await accountOpeningService.RetryAsync(id, cancellationToken);
        return response.Status == nameof(AccountOpeningStatus.Failed)
            ? UnprocessableEntity(response)
            : Ok(response);
    }
}
