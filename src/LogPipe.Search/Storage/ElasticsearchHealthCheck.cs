using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LogPipe.Search.Storage;

public sealed class ElasticsearchHealthCheck(ElasticsearchClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.PingAsync(cancellationToken);
            return response.IsValidResponse
                ? HealthCheckResult.Healthy("elasticsearch reachable")
                : HealthCheckResult.Unhealthy("elasticsearch ping failed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("elasticsearch unreachable", ex);
        }
    }
}
