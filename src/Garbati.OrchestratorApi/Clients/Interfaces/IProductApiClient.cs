using Garbati.OrchestratorApi.Clients.Contracts.Product;

namespace Garbati.OrchestratorApi.Clients.Interfaces;

// This service as a client of Garbati.ProductApi (DESIGN.md Section 1.3 step 5, T-046). One method for
// this task: binding a newly-opened ledger account to a product code. Mirrors ILedgerApiClient's shape.
public interface IProductApiClient
{
    Task<AccountProductBindingResponse> BindAccountToProductAsync(
        BindAccountToProductRequest request, CancellationToken cancellationToken);
}
