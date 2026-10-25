using Garbati.OrchestratorApi.Clients.Contracts.Product;
using Garbati.OrchestratorApi.Clients.Interfaces;

namespace Garbati.OrchestratorApi.Tests.Fixtures;

// See FakeIdentityApiClient's comment: same hand-rolled-fake approach, for the new T-046 BindProduct step.
public class FakeProductApiClient : IProductApiClient
{
    public int BindAccountToProductCallCount { get; private set; }

    public Func<BindAccountToProductRequest, AccountProductBindingResponse>? BindAccountToProductHandler { get; set; }

    public Task<AccountProductBindingResponse> BindAccountToProductAsync(BindAccountToProductRequest request, CancellationToken cancellationToken)
    {
        BindAccountToProductCallCount++;
        var handler = BindAccountToProductHandler ?? (r => new AccountProductBindingResponse(
            r.AccountNumber, r.ProductCode, DateTimeOffset.UtcNow));
        return Task.FromResult(handler(request));
    }
}
