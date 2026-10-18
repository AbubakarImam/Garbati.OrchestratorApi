using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;

namespace Garbati.OrchestratorApi.Services;

public interface IAccountOpeningService
{
    Task<AccountOpeningResponse> StartAsync(StartAccountOpeningRequest request, CancellationToken cancellationToken = default);

    Task<AccountOpeningResponse?> GetAsync(string operationId, CancellationToken cancellationToken = default);

    Task<AccountOpeningResponse> RetryAsync(string operationId, CancellationToken cancellationToken = default);
}
