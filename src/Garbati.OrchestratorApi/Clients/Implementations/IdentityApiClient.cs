using System.Net;
using Garbati.OrchestratorApi.Clients.Contracts.Identity;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Exceptions;

namespace Garbati.OrchestratorApi.Clients.Implementations;

// Talks to IdentityApi over HTTP. The HttpClient is configured (BaseAddress, X-Api-Key header,
// X-Correlation-ID propagation) at registration time in Program.cs - this class only knows the paths and
// how to turn a response into either a typed result or one of this service's own exceptions.
public class IdentityApiClient(HttpClient httpClient) : IIdentityApiClient
{
    public async Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("v1/customers", request, cancellationToken);
        return await ReadOrThrowAsync<CustomerResponse>(response, "IdentityApi POST /v1/customers", cancellationToken);
    }

    public async Task<CustomerResponse> RecordKycDecisionAsync(
        string customerId, KycDecisionRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PatchAsJsonAsync($"v1/customers/{customerId}/kyc", request, cancellationToken);
        return await ReadOrThrowAsync<CustomerResponse>(
            response, $"IdentityApi PATCH /v1/customers/{customerId}/kyc", cancellationToken);
    }

    // DESIGN.md Section 1.3 step 4 / task file: on a 409 here, IdentityApi is reporting that this
    // customer already has an active mandate on this account (Garbati.IdentityApi.Exceptions.
    // DuplicateActiveMandateException). AttachMandateRequest has no idempotency key, so this is the only
    // signal a retry gets that the step already succeeded - AccountOpeningService's retry path decides
    // what to do with it, this client just surfaces it as a distinguishable exception rather than a
    // generic DownstreamApiException.
    public async Task<MandateResponse> AttachMandateAsync(
        string customerId, AttachMandateRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"v1/customers/{customerId}/mandates", request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
            throw new MandateAlreadyAttachedException(DownstreamResponseReader.DescribeFailure(problem, response));
        }

        return await ReadOrThrowAsync<MandateResponse>(
            response, $"IdentityApi POST /v1/customers/{customerId}/mandates", cancellationToken);
    }

    // DESIGN.md Section 1.5 step 1: a 422 here is IdentityApi denying the transfer
    // (Garbati.IdentityApi.Exceptions.AuthorizationDeniedException on its side) - a normal, expected
    // outcome, not an error to wrap generically. DecisionId/ReasonCode come back as ProblemDetails
    // extensions (confirmed against IdentityApi's live ExceptionHandlingMiddleware).
    public async Task<AuthorizationResponse> RequestAuthorizationAsync(
        AuthorizationRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("v1/authorization/requests", request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
            throw new AuthorizationDeniedException(
                problem?.ExtensionString("decisionId"),
                problem?.ExtensionString("reasonCode"),
                DownstreamResponseReader.DescribeFailure(problem, response));
        }

        return await ReadOrThrowAsync<AuthorizationResponse>(
            response, "IdentityApi POST /v1/authorization/requests", cancellationToken);
    }

    // T-030/DESIGN.md Section 8.6 on IdentityApi's side: reports the real LedgerApi outcome back, already
    // built there (identity.ops.reversal policy - same scope the authorization-request call above needs).
    // No special status handling here: unlike RequestAuthorizationAsync's 422-means-denied case, a bad
    // Outcome value is rejected with a plain 400 by IdentityApi's own RecordReversalOutcomeRequestValidator,
    // which this service's own ReversalOutcomeKind-shaped literals ("Completed"/"WriteOff") never trigger.
    public async Task<ReversalOutcomeResponse> RecordReversalOutcomeAsync(
        string decisionId, RecordReversalOutcomeRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"v1/authorization/requests/{decisionId}/reversal-outcome", request, cancellationToken);
        return await ReadOrThrowAsync<ReversalOutcomeResponse>(
            response, $"IdentityApi POST /v1/authorization/requests/{decisionId}/reversal-outcome", cancellationToken);
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new DownstreamApiException("IdentityApi", (int)response.StatusCode, $"{what}: empty response body.");
        }

        var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
        throw new DownstreamApiException(
            "IdentityApi", (int)response.StatusCode, $"{what}: {DownstreamResponseReader.DescribeFailure(problem, response)}");
    }
}
