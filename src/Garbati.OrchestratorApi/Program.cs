using Garbati.OrchestratorApi.Auditing;
using Garbati.OrchestratorApi.Authorization;
using Garbati.OrchestratorApi.Clients.Implementations;
using Garbati.OrchestratorApi.Clients.Interfaces;
using Garbati.OrchestratorApi.Configuration;
using Garbati.OrchestratorApi.Data;
using Garbati.OrchestratorApi.Middleware;
using Garbati.OrchestratorApi.Services;
using Garbati.OrchestratorApi.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// Bootstrap logger: records errors that happen before configuration is loaded (e.g. a broken appsettings file).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default logging with Serilog, configured from the "Serilog" section of appsettings.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers(options => options.Filters.Add<IdempotencyKeyLoggingFilter>());
    // Lowercase route URLs in generated links and the Swagger docs, same as both sibling services.
    builder.Services.AddRouting(options => options.LowercaseUrls = true);
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        // Adds an "Authorize" button to Swagger UI that sends the X-Api-Key header - this is the key a
        // caller of THIS service presents, not either downstream service's key.
        options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationHandler.HeaderName,
            Description = "API key issued to the calling client (e.g. the demo driver)."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyAuthenticationHandler.SchemeName }
            }] = []
        });
    });

    // Authentication: who is calling THIS service (API key -> client id + scopes). Same pattern as
    // LedgerApi/IdentityApi.
    builder.Services.AddOptions<ApiKeyOptions>()
        .Bind(builder.Configuration.GetSection("ApiKeys"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<ApiKeyOptions>, ApiKeyOptionsValidator>();

    // This service's OWN outbound credentials for calling IdentityApi/LedgerApi (Configuration/
    // DownstreamServicesOptions.cs) - a different concern from ApiKeys above.
    builder.Services.AddOptions<DownstreamServicesOptions>()
        .Bind(builder.Configuration.GetSection("DownstreamServices"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<DownstreamServicesOptions>, DownstreamServicesOptionsValidator>();

    builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

    // Authorization: one policy per scope, plus a fallback so an endpoint without [Authorize] is never public.
    builder.Services.AddAuthorization(options =>
    {
        foreach (var scope in OrchestratorScopes.All)
            options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(OrchestratorScopes.ClaimType, scope));

        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });

    builder.Services.AddDbContext<OrchestratorDbContext>(options => options
        .UseNpgsql(builder.Configuration.GetConnectionString("OrchestratorDb"))
        .UseSnakeCaseNamingConvention());

    // AuditLogger reads the actor, IP, user agent and correlation id of the current request; the same
    // accessor also lets CorrelationIdPropagationHandler read the id for outgoing calls below.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentClient, HttpCurrentClient>();

    // Health checks for the hosting platform: "ready" includes the database, "live" checks only the process.
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<OrchestratorDbContext>("database", tags: ["ready"]);

    builder.Services.AddScoped<IAccountOpeningService, AccountOpeningService>();
    builder.Services.AddScoped<ITransferOrchestrationService, TransferService>();
    builder.Services.AddScoped<IReversalOrchestrationService, ReversalService>();
    builder.Services.AddScoped<IAuditLogger, AuditLogger>();

    builder.Services.AddScoped<StartAccountOpeningRequestValidator>();
    builder.Services.AddScoped<TransferRequestValidator>();
    builder.Services.AddScoped<ReversalRequestValidator>();

    // This service as a client of IdentityApi/LedgerApi (task file: "an HTTP client setup ... configured
    // with this service's own API key for each"). BaseAddress and the outgoing X-Api-Key value come from
    // DownstreamServicesOptions, resolved once per HttpClient instance from the pool.
    builder.Services.AddTransient<CorrelationIdPropagationHandler>();

    builder.Services.AddHttpClient<IIdentityApiClient, IdentityApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value.IdentityApi;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, options.ApiKey);
    }).AddHttpMessageHandler<CorrelationIdPropagationHandler>();

    builder.Services.AddHttpClient<ILedgerApiClient, LedgerApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value.LedgerApi;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, options.ApiKey);
    }).AddHttpMessageHandler<CorrelationIdPropagationHandler>();

    // T-046: this service as a client of Garbati.ProductApi (the new BindProduct account-opening step).
    builder.Services.AddHttpClient<IProductApiClient, ProductApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value.ProductApi;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, options.ApiKey);
    }).AddHttpMessageHandler<CorrelationIdPropagationHandler>();

    var app = builder.Build();

    // First in the pipeline, so every later log line (including the request summary) has the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // One structured log line per request: method, path, status code and elapsed time, plus these extras.
    app.UseSerilogRequestLogging(options =>
    {
        // Platforms probe /health every few seconds; log those only when they fail.
        options.GetLevel = (httpContext, _, exception) =>
            exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information;

        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId", httpContext.User.Identity?.Name ?? "anonymous");
            diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.EnablePersistAuthorization());
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // No UseHttpsRedirection: a redirect cannot protect an API key that was already sent over HTTP. TLS
    // belongs to the deployment (proxy/ingress or Kestrel), same as both sibling services.

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Anonymous on purpose: platforms probe without an API key. Live = the process responds; ready = it
    // can reach the database.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Orchestrator API terminated unexpectedly");
    // Non-zero so Docker, Kubernetes and CI see a failed start rather than a clean stop.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Lets the test project start the app in memory with WebApplicationFactory<Program>.
public partial class Program;
