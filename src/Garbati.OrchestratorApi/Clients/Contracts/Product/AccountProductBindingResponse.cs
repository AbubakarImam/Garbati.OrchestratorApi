namespace Garbati.OrchestratorApi.Clients.Contracts.Product;

// Mirrors Garbati.ProductApi.Contracts.Responses.AccountProductBindingResponse.
public record AccountProductBindingResponse(
    string AccountNumber,
    string ProductCode,
    DateTimeOffset BoundAt);
