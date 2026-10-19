using Garbati.OrchestratorApi.Contracts.Requests;
using Garbati.OrchestratorApi.Contracts.Responses;

namespace Garbati.OrchestratorApi.Services;

public interface IReversalOrchestrationService
{
    Task<ReversalResultResponse> ReverseAsync(ReversalRequest request, CancellationToken cancellationToken = default);
}
