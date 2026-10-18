using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;

namespace Garbati.OrchestratorApi.Services;

public interface ITransferOrchestrationService
{
    Task<TransferResultResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);
}
