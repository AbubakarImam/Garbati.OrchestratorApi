using Garbati.OrchestratorApi.Clients.Contracts.Ledger;
using Garbati.OrchestratorApi.Clients.Interfaces;

namespace Garbati.OrchestratorApi.Tests.Fixtures;

// See FakeIdentityApiClient's comment: same hand-rolled-fake approach.
public class FakeLedgerApiClient : ILedgerApiClient
{
    public int CreateAccountCallCount { get; private set; }
    public int TransferCallCount { get; private set; }

    public Func<CreateAccountRequest, AccountResponse>? CreateAccountHandler { get; set; }
    public Func<TransferRequest, TransferResponse>? TransferHandler { get; set; }

    public Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        CreateAccountCallCount++;
        var handler = CreateAccountHandler ?? (r => new AccountResponse(
            $"{CreateAccountCallCount:D10}9999", r.AccountName, r.AccountType, "Customer", r.CurrencyCode,
            "Active", DateTimeOffset.UtcNow));
        return Task.FromResult(handler(request));
    }

    public Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        TransferCallCount++;
        var handler = TransferHandler ?? (r => new TransferResponse($"TXN-FAKE{TransferCallCount:D8}", "Success"));
        return Task.FromResult(handler(request));
    }
}
