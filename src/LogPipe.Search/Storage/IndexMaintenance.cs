using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;

namespace LogPipe.Search.Storage;

/// <summary>
/// Code-managed retention + alias (ADR-0003): hourly, points logs-current at
/// today's index and deletes logs-* indices older than Retention:Days.
/// </summary>
public sealed class IndexMaintenance(
    ElasticsearchClient client, IConfiguration config, ILogger<IndexMaintenance> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Index maintenance run failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var today = IndexNaming.IndexName(DateTimeOffset.UtcNow);

        // The alias target must exist before the atomic swap; the template supplies mappings.
        var todayExists = await client.Indices.ExistsAsync(today, ct);
        if (!todayExists.Exists)
        {
            await client.Indices.CreateAsync(today, cancellationToken: ct);
        }

        // ES requires index/indices on remove actions, so collect current alias targets first.
        var current = await client.Indices.GetAliasAsync((Names)IndexNaming.CurrentAlias, ct);
        var stale = current.IsValidResponse && current.Aliases is not null
            ? current.Aliases.Keys.Where(name => name != today).ToList()
            : [];

        var aliasActions = new List<Action<IndexUpdateAliasesActionDescriptor>>
        {
            d => d.Add(a => a.Index(today).Alias(IndexNaming.CurrentAlias))
        };
        foreach (var indexName in stale)
        {
            var captured = indexName;
            aliasActions.Add(d => d.Remove(r => r.Index(captured).Alias(IndexNaming.CurrentAlias)));
        }

        // Reassign the alias atomically in one update call.
        var aliasResponse = await client.Indices.UpdateAliasesAsync(
            update => update.Actions(aliasActions.ToArray()), ct);
        if (!aliasResponse.IsValidResponse)
        {
            logger.LogWarning("Alias reassignment failed: {Status}",
                aliasResponse.ApiCallDetails?.HttpStatusCode);
        }

        var retentionDays = config.GetValue("Retention:Days", 7);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).Date;
        var existing = await client.Indices.GetAsync(IndexNaming.ReadPattern, ct);
        foreach (var indexName in existing.Indices.Keys)
        {
            if (!IndexNaming.TryParseDate(indexName, out var date)) continue;
            if (date >= cutoff) continue;
            var deleteResponse = await client.Indices.DeleteAsync(indexName, ct);
            logger.LogInformation("Retention: deleted {Index} (ok: {Ok})",
                indexName, deleteResponse.IsValidResponse);
        }
    }
}
