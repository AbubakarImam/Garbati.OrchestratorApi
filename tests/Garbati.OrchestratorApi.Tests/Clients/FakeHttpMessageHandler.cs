using System.Net;
using System.Text;

namespace Garbati.OrchestratorApi.Tests.Clients;

// A hand-rolled fake HttpMessageHandler (no mocking library, matching project convention) for testing
// IdentityApiClient/LedgerApiClient's own request-building and response-parsing logic in isolation, without
// a real network call. This is deliberately narrower than the service-level fakes in Fixtures/ - those
// stand in for "the downstream service" from the Services/ layer's point of view; these stand in for "the
// network" from the Clients/ layer's point of view, so the client classes' own HTTP/JSON handling gets
// direct coverage too.
public class FakeHttpMessageHandler(HttpStatusCode statusCode, string? jsonBody, string contentType = "application/json")
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var response = new HttpResponseMessage(statusCode);
        if (jsonBody is not null)
        {
            response.Content = new StringContent(jsonBody, Encoding.UTF8, contentType);
        }
        return response;
    }
}
