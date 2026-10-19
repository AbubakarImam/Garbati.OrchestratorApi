using Garbati.OrchestratorApi.Clients.Contracts.Identity;
using Garbati.OrchestratorApi.Clients.Interfaces;

namespace Garbati.OrchestratorApi.Tests.Fixtures;

// A hand-rolled fake, not a mocking library (matching LedgerApi's/IdentityApi's own test style - neither
// project references Moq/NSubstitute). Each method has a sensible default and an overridable handler, so
// a test can simulate a specific step's failure while every earlier/later step keeps working normally,
// and assert on call counts (task file: "verify this with a call-count assertion on your HTTP client
// mock/stub, not just the end state").
public class FakeIdentityApiClient : IIdentityApiClient
{
    public int CreateCustomerCallCount { get; private set; }
    public int RecordKycDecisionCallCount { get; private set; }
    public int AttachMandateCallCount { get; private set; }
    public int RequestAuthorizationCallCount { get; private set; }
    public int RecordReversalOutcomeCallCount { get; private set; }

    public Func<CreateCustomerRequest, CustomerResponse>? CreateCustomerHandler { get; set; }
    public Func<string, KycDecisionRequest, CustomerResponse>? RecordKycDecisionHandler { get; set; }
    public Func<string, AttachMandateRequest, MandateResponse>? AttachMandateHandler { get; set; }
    public Func<AuthorizationRequest, AuthorizationResponse>? RequestAuthorizationHandler { get; set; }
    public Func<string, RecordReversalOutcomeRequest, ReversalOutcomeResponse>? RecordReversalOutcomeHandler { get; set; }

    public Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        CreateCustomerCallCount++;
        var handler = CreateCustomerHandler ?? (r => new CustomerResponse(
            $"CUS-FAKE{CreateCustomerCallCount:D10}", r.CustomerReference, "Active", "Pending", null,
            r.JurisdictionCode, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return Task.FromResult(handler(request));
    }

    public Task<CustomerResponse> RecordKycDecisionAsync(string customerId, KycDecisionRequest request, CancellationToken cancellationToken)
    {
        RecordKycDecisionCallCount++;
        var handler = RecordKycDecisionHandler ?? ((id, r) => new CustomerResponse(
            id, null, "Active", r.KycStatus, r.KycTier, "NG", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return Task.FromResult(handler(customerId, request));
    }

    public Task<MandateResponse> AttachMandateAsync(string customerId, AttachMandateRequest request, CancellationToken cancellationToken)
    {
        AttachMandateCallCount++;
        var handler = AttachMandateHandler ?? ((id, r) => new MandateResponse(
            $"MND-FAKE{AttachMandateCallCount:D10}", id, r.LedgerAccountId, "Active", r.Operations,
            DateTimeOffset.UtcNow, "test-client", null, null, null));
        return Task.FromResult(handler(customerId, request));
    }

    public Task<AuthorizationResponse> RequestAuthorizationAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        RequestAuthorizationCallCount++;
        var handler = RequestAuthorizationHandler ?? (r => new AuthorizationResponse(
            new AuthorizationDecisionResponse(
                $"DEC-FAKE{RequestAuthorizationCallCount:D10}", true, null, r.RequestedBy, r.Operation,
                r.DebitAccountId, r.CreditAccountId, r.Amount.ToString("F2"), r.Currency, "MND-FAKE0000000001", DateTimeOffset.UtcNow),
            "fake.grant.token",
            DateTimeOffset.UtcNow.AddSeconds(30)));
        return Task.FromResult(handler(request));
    }

    public Task<ReversalOutcomeResponse> RecordReversalOutcomeAsync(
        string decisionId, RecordReversalOutcomeRequest request, CancellationToken cancellationToken)
    {
        RecordReversalOutcomeCallCount++;
        var handler = RecordReversalOutcomeHandler ?? ((id, r) => new ReversalOutcomeResponse(
            id, r.Outcome, DateTimeOffset.UtcNow, r.RecordedBy, r.Notes));
        return Task.FromResult(handler(decisionId, request));
    }
}
