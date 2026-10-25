using Garbati.OrchestratorApi.Clients.Contracts.Product;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Exceptions;

namespace Garbati.OrchestratorApi.Clients.Implementations;

// Talks to Garbati.ProductApi over HTTP. See IdentityApiClient's comment on how the HttpClient is
// configured. T-046: unlike LedgerApiClient.TransferAsync, POST /v1/account-bindings has no dual
// "200 or 422-with-body" case to special-case - Garbati.ProductApi's own AccountProductBindingService
// either returns the binding (200, including on an idempotent-key replay) or throws before building a
// response (404 ProductNotFound, 409 AccountAlreadyBound/duplicate-idempotency-key-with-a-different-body)
// - confirmed against its live source, not assumed. So any non-success response is surfaced as
// DownstreamApiException, the same shape as LedgerApiClient.CreateAccountAsync/ReverseAsync.
public class ProductApiClient(HttpClient httpClient) : IProductApiClient
{
    public async Task<AccountProductBindingResponse> BindAccountToProductAsync(
        BindAccountToProductRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("v1/account-bindings", request, cancellationToken);
        return await ReadOrThrowAsync<AccountProductBindingResponse>(response, "ProductApi POST /v1/account-bindings", cancellationToken);
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new DownstreamApiException("ProductApi", (int)response.StatusCode, $"{what}: empty response body.");
        }

        var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
        throw new DownstreamApiException(
            "ProductApi", (int)response.StatusCode, $"{what}: {DownstreamResponseReader.DescribeFailure(problem, response)}");
    }
}
