namespace Garbati.OrchestratorApi.Contracts.Requests;

// Marks a request that carries a client idempotency key, so it can be logged without logging the body.
public interface IIdempotentRequest
{
    string IdempotencyKey { get; }
}
