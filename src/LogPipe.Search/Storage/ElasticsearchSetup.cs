using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace LogPipe.Search.Storage;

public sealed class ElasticsearchSetup(
    ElasticsearchClient client, ILogger<ElasticsearchSetup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var response = await client.Indices.PutIndexTemplateAsync("logpipe-logs", descriptor => descriptor
            .IndexPatterns(new[] { IndexNaming.ReadPattern })
            // Must not collide with the built-in Elastic "logs" template (logs-*-* @ priority 100).
            .Priority(500)
            .Template(template => template
                .Settings(settings => settings
                    .NumberOfShards("1")
                    .NumberOfReplicas("0")) // single-node demo: replica=1 would stay yellow forever
                .Mappings(mappings => mappings
                    .Properties(properties => properties
                        .Keyword("id")
                        .Keyword("level")
                        .Keyword("service")
                        .Keyword("trace_id")
                        .Keyword("span_id")
                        .Date("timestamp")
                        .Text("message")
                        .Object("attributes", attribute => attribute.Dynamic(DynamicMapping.True))))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException(
                $"Failed to put index template 'logpipe-logs': {response.ApiCallDetails?.HttpStatusCode}");
        }

        logger.LogInformation("Index template 'logpipe-logs' applied");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
