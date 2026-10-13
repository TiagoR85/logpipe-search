using Grpc.Core;
using LogPipe.Contracts.V1;

namespace LogPipe.Search.Grpc;

public sealed class LogIngestServiceImpl(
    IngestedEntryProcessor processor, ILogger<LogIngestServiceImpl> logger)
    : LogIngest.LogIngestBase
{
    public override async Task<StreamAck> StreamLogs(
        IAsyncStreamReader<LogEntry> requestStream, ServerCallContext context)
    {
        var entries = new List<LogEntry>();
        await foreach (var entry in requestStream.ReadAllAsync(context.CancellationToken))
        {
            entries.Add(entry);
        }

        var (accepted, rejected) = await processor.IndexAsync(entries, context.CancellationToken);
        logger.LogInformation("Indexed {Accepted} entries ({Rejected} rejected) from {Caller}",
            accepted, rejected, context.Peer);
        return new StreamAck { Accepted = accepted, Rejected = rejected };
    }
}
