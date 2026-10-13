using Elastic.Clients.Elasticsearch;
using Grpc.Core;
using LogPipe.Contracts.V1;

namespace LogPipe.Search.Tests;

[Collection("containers")]
public sealed class IndexingTests
{
    private readonly ContainersFixture _fixture;
    public IndexingTests(ContainersFixture fixture) => _fixture = fixture;

    private static LogEntry NewEntry(string service, string? id = null, string? traceId = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow.ToString("O"),
        Level = "Information",
        Service = service,
        Message = "indexed hello",
        TraceId = traceId ?? "",
        SpanId = ""
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Streamed_entries_are_indexed_and_counted_in_ack()
    {
        var service = $"indexing-{Guid.NewGuid():N}";
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        var client = factory.CreateClient(); // host start runs ElasticsearchSetup (template)
        var stub = new LogIngest.LogIngestClient(ContainersFixture.CreateGrpcChannel(factory));

        using var call = stub.StreamLogs();
        await call.RequestStream.WriteAsync(NewEntry(service));
        await call.RequestStream.WriteAsync(NewEntry(service));
        await call.RequestStream.CompleteAsync();
        var ack = await call.ResponseAsync;

        Assert.Equal(2, ack.Accepted);
        Assert.Equal(0, ack.Rejected);

        var es = new ElasticsearchClient(new ElasticsearchClientSettings(new Uri(_fixture.ElasticsearchUrl)));
        await WaitUntilAsync(async () =>
        {
            var search = await es.SearchAsync<LogPipe.Search.Storage.LogDocument>(s => s
                .Indices("logs-*")
                .Query(q => q.Term(t => t.Field("service").Value(service)))
                .Size(10));
            return search.Hits.Count == 2;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Invalid_entries_are_rejected_not_thrown()
    {
        var service = $"indexing-{Guid.NewGuid():N}";
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        factory.CreateClient();
        var stub = new LogIngest.LogIngestClient(ContainersFixture.CreateGrpcChannel(factory));

        using var call = stub.StreamLogs();
        await call.RequestStream.WriteAsync(new LogEntry
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Level = "Bogus",
            Service = service,
            Message = "bad level"
        });
        await call.RequestStream.WriteAsync(NewEntry(service));
        await call.RequestStream.CompleteAsync();
        var ack = await call.ResponseAsync;

        Assert.Equal(1, ack.Accepted);
        Assert.Equal(1, ack.Rejected);
    }

    [Fact]
    public async Task Resending_same_id_is_idempotent_overwrite()
    {
        var service = $"indexing-{Guid.NewGuid():N}";
        var id = Guid.NewGuid().ToString();
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        factory.CreateClient();
        var stub = new LogIngest.LogIngestClient(ContainersFixture.CreateGrpcChannel(factory));

        using (var first = stub.StreamLogs())
        {
            await first.RequestStream.WriteAsync(NewEntry(service, id));
            await first.RequestStream.CompleteAsync();
            await first.ResponseAsync;
        }
        using (var second = stub.StreamLogs())
        {
            await second.RequestStream.WriteAsync(NewEntry(service, id));
            await second.RequestStream.CompleteAsync();
            var ack = await second.ResponseAsync;
            Assert.Equal(1, ack.Accepted);
        }

        var es = new ElasticsearchClient(new ElasticsearchClientSettings(new Uri(_fixture.ElasticsearchUrl)));
        await WaitUntilAsync(async () =>
        {
            var count = await es.CountAsync<LogPipe.Search.Storage.LogDocument>(c => c
                .Indices("logs-*")
                .Query(q => q.Term(t => t.Field("service").Value(service))));
            return count.Count == 1;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Oversized_batch_fails_with_invalid_argument()
    {
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        factory.CreateClient();
        var stub = new LogIngest.LogIngestClient(ContainersFixture.CreateGrpcChannel(factory));

        using var call = stub.StreamLogs();
        for (var i = 0; i <= LogPipe.Contracts.Validation.LogEntryRules.MaxEntriesPerBatch; i++)
            await call.RequestStream.WriteAsync(NewEntry($"oversized-{i}"));
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }
}
