using Garbati.OrchestratorApi.Clients.Contracts.Ledger;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Exceptions;

namespace Garbati.OrchestratorApi.Clients.Implementations;

// Talks to LedgerApi over HTTP. See IdentityApiClient's comment on how the HttpClient is configured.
public class LedgerApiClient(HttpClient httpClient) : ILedgerApiClient
{
    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/accounts", request, cancellationToken);
        return await ReadOrThrowAsync<AccountResponse>(response, "LedgerApi POST /api/accounts", cancellationToken);
    }

    // LedgerApi's TransfersController maps its own TransactionStatus.Failed result to HTTP 422, but the
    // body is still a normal TransferResponse describing the failed leg (ARCHITECTURE.md Section 4.1's
    // "a business-rule failure is committed and returned like a success, but with 422" - confirmed against
    // the live controller, not assumed). That is data for TransferService to act on, not an exception -
    // a 422 here is read the same way as a 200, unlike IdentityApiClient's 422 handling.
    public async Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/transfers", request, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return await response.Content.ReadFromJsonAsync<TransferResponse>(cancellationToken)
                ?? throw new DownstreamApiException("LedgerApi", (int)response.StatusCode, "LedgerApi POST /api/transfers: empty response body.");
        }

        var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
        throw new DownstreamApiException(
            "LedgerApi", (int)response.StatusCode,
            $"LedgerApi POST /api/transfers: {DownstreamResponseReader.DescribeFailure(problem, response)}");
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new DownstreamApiException("LedgerApi", (int)response.StatusCode, $"{what}: empty response body.");
        }

        var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
        throw new DownstreamApiException(
            "LedgerApi", (int)response.StatusCode, $"{what}: {DownstreamResponseReader.DescribeFailure(problem, response)}");
    }
}
