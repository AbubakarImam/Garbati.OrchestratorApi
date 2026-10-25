using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Garbati.OrchestratorApi.Tests.Fixtures;

// Runs the real app in memory (WebApplicationFactory) against its own Postgres container, so requests go
// through the full pipeline: correlation id, authentication, authorization, controllers and the database.
//
// IIdentityApiClient/ILedgerApiClient/IProductApiClient are replaced with FakeIdentityApiClient/
// FakeLedgerApiClient/FakeProductApiClient here (task file's judgment call: this service is tested
// against mocked/stubbed HTTP clients, not real locally-run IdentityApi/LedgerApi/ProductApi instances -
// see the PR description for the reasoning and what that means this suite cannot verify).
// DownstreamServices:* configuration is still filled with syntactically valid placeholder values below
// purely so DownstreamServicesOptionsValidator's ValidateOnStart passes - those values are never
// actually dialled.
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeIdentityApiClient IdentityApiClient { get; } = new();
    public FakeLedgerApiClient LedgerApiClient { get; } = new();
    public FakeProductApiClient ProductApiClient { get; } = new();

    // For every scope there is a client holding only that scope, and one holding every scope except it.
    public static string OnlyClient(string scope) => $"only-{scope}";
    public static string AllButClient(string scope) => $"all-but-{scope}";
    public static string KeyFor(string clientId) => $"test-key-{clientId}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var ctx = CreateContext())
        {
            await ctx.Database.MigrateAsync();
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OrchestratorDb"] = _container.GetConnectionString(),
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Swagger:Enabled"] = "true",
            ["DownstreamServices:IdentityApi:BaseUrl"] = "http://unused.invalid",
            ["DownstreamServices:IdentityApi:ApiKey"] = "unused-placeholder",
            ["DownstreamServices:LedgerApi:BaseUrl"] = "http://unused.invalid",
            ["DownstreamServices:LedgerApi:ApiKey"] = "unused-placeholder",
            ["DownstreamServices:ProductApi:BaseUrl"] = "http://unused.invalid",
            ["DownstreamServices:ProductApi:ApiKey"] = "unused-placeholder",
        };

        var clients = OrchestratorScopes.All
            .SelectMany(scope => new[]
            {
                (Id: OnlyClient(scope), Scopes: new[] { scope }),
                (Id: AllButClient(scope), Scopes: OrchestratorScopes.All.Where(s => s != scope).ToArray()),
            })
            .ToList();

        for (var i = 0; i < clients.Count; i++)
        {
            settings[$"ApiKeys:Clients:{i}:ClientId"] = clients[i].Id;
            settings[$"ApiKeys:Clients:{i}:KeyHash"] = Convert.ToBase64String(ApiKeyHasher.Hash(KeyFor(clients[i].Id)));
            for (var j = 0; j < clients[i].Scopes.Length; j++)
            {
                settings[$"ApiKeys:Clients:{i}:Scopes:{j}"] = clients[i].Scopes[j];
            }
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityApiClient>();
                services.AddSingleton<IIdentityApiClient>(IdentityApiClient);
                services.RemoveAll<ILedgerApiClient>();
                services.AddSingleton<ILedgerApiClient>(LedgerApiClient);
                services.RemoveAll<IProductApiClient>();
                services.AddSingleton<IProductApiClient>(ProductApiClient);
            });
        });
    }

    public OrchestratorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new OrchestratorDbContext(options);
    }

    // An HTTP client for the in-memory app, sending the given client's key (or no key when null).
    public HttpClient CreateClient(string? clientId)
    {
        var client = Factory.CreateClient();
        if (clientId is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, KeyFor(clientId));
        }
        return client;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
