namespace Garbati.OrchestratorApi.Clients.Contracts.Product;

// Mirrors Garbati.ProductApi.Contracts.Requests.CreateAccountProductBindingRequest exactly (read directly
// - task file: "matching Garbati.ProductApi's own CreateAccountProductBindingRequest shape exactly").
public record BindAccountToProductRequest(
    string IdempotencyKey,
    string AccountNumber,
    string ProductCode);
