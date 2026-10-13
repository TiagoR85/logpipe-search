using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Grpc.Core;
using LogPipe.Contracts.Validation;
using LogPipe.Contracts.V1;
using LogPipe.Search.Storage;

namespace LogPipe.Search.Grpc;

/// <summary>
/// Validates entries with the shared contract rules and bulk-indexes them into
/// today's index. Doc id = entry id, so ingest retries are idempotent overwrites.
/// </summary>
public sealed class IngestedEntryProcessor(
    ElasticsearchClient client, ILogger<IngestedEntryProcessor> logger)
{
    public async Task<(int Accepted, int Rejected)> IndexAsync(
        IReadOnlyList<LogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count > LogEntryRules.MaxEntriesPerBatch)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"at most {LogEntryRules.MaxEntriesPerBatch} entries per batch"));
        }

        var docs = new List<LogDocument>(entries.Count);
        var rejected = 0;
        foreach (var entry in entries)
        {
            if (!LogEntryRules.TryValidate(entry, out _))
            {
                rejected++;
                continue;
            }

            docs.Add(new LogDocument
            {
                Id = entry.Id,
                Timestamp = string.IsNullOrEmpty(entry.Timestamp)
                    ? DateTimeOffset.UtcNow
                    : DateTimeOffset.Parse(entry.Timestamp, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal),
                Level = entry.Level,
                Service = entry.Service,
                Message = entry.Message,
                TraceId = string.IsNullOrEmpty(entry.TraceId) ? null : entry.TraceId,
                SpanId = string.IsNullOrEmpty(entry.SpanId) ? null : entry.SpanId,
                Attributes = entry.Attributes.Count == 0
                    ? null
                    : new Dictionary<string, string>(entry.Attributes)
            });
        }

        if (docs.Count == 0) return (0, rejected);

        var indexName = IndexNaming.IndexName(DateTimeOffset.UtcNow);
        var response = await client.BulkAsync(bulk => bulk
            .Index(indexName)
            .IndexMany(docs, (operation, document) => operation.Id(document.Id)),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            SearchMetrics.BulkFailures.Add(1);
            logger.LogError("Bulk index into {Index} failed: {Status}",
                indexName, response.ApiCallDetails?.HttpStatusCode);
            // Unavailable → the ingest forwarder retries (ingest ADR-0003 owns the give-up path).
            throw new RpcException(new Status(StatusCode.Unavailable,
                "elasticsearch bulk index failed"));
        }

        SearchMetrics.Indexed.Add(docs.Count);
        SearchMetrics.Rejected.Add(rejected);
        return (docs.Count, rejected);
    }
}
