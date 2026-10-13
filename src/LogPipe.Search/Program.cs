using Elastic.Clients.Elasticsearch;
using LogPipe.Search.Grpc;
using LogPipe.Search.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration (same rule as ingest): never start half-configured.
string[] required = ["Elasticsearch:Url", "Jwt:Authority"];
var missing = required
    .Where(key => string.IsNullOrWhiteSpace(builder.Configuration[key]))
    .ToArray();
if (missing.Length > 0)
{
    throw new InvalidOperationException(
        $"Missing required configuration: {string.Join(", ", missing)}. See .env.example.");
}

builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.AddSingleton(_ =>
{
    var url = builder.Configuration["Elasticsearch:Url"]!;
    return new ElasticsearchClient(new ElasticsearchClientSettings(new Uri(url)));
});

builder.Services.AddSingleton<ElasticsearchHealthCheck>();
builder.Services.AddHealthChecks()
    .AddCheck("self", static () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<ElasticsearchHealthCheck>("elasticsearch", tags: ["ready"]);

// Standard OIDC discovery against the ingest issuer (ADR-0002) — no shared secret,
// no ValidateIssuer=false hack. Scope policies are enforced in Task 8.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        // Demo hub runs plain HTTP internally; production flips this back on (ADR-0002).
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Jwt:RequireHttpsMetadata", true);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false // OpenIddict access tokens carry scopes, not aud, in F2
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IngestedEntryProcessor>();
builder.Services.AddGrpc();
builder.Services.AddHostedService<ElasticsearchSetup>();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("LogPipe.Search")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource("LogPipe.Search"));

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static r => r.Tags.Contains("ready") });
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");
app.MapGrpcService<LogIngestServiceImpl>();

app.Run();

public partial class Program;
