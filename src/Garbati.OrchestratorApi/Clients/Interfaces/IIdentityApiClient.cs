using Garbati.OrchestratorApi.Clients.Contracts.Identity;

namespace Garbati.OrchestratorApi.Clients.Interfaces;

// This service as a client of IdentityApi (DESIGN.md Section 1.3/1.5/1.10). One method per downstream
// call the account-opening and transfer flows actually make - not a general-purpose IdentityApi SDK.
public interface IIdentityApiClient
{
    Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken);

    Task<CustomerResponse> RecordKycDecisionAsync(
        string customerId, KycDecisionRequest request, CancellationToken cancellationToken);

    Task<MandateResponse> AttachMandateAsync(
        string customerId, AttachMandateRequest request, CancellationToken cancellationToken);

    Task<AuthorizationResponse> RequestAuthorizationAsync(
        AuthorizationRequest request, CancellationToken cancellationToken);
}
