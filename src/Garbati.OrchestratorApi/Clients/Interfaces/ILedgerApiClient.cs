using Garbati.OrchestratorApi.Clients.Contracts.Ledger;

namespace Garbati.OrchestratorApi.Clients.Interfaces;

// This service as a client of LedgerApi (DESIGN.md Section 1.3/1.5/1.10).
public interface ILedgerApiClient
{
    Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken);

    Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken);
}
