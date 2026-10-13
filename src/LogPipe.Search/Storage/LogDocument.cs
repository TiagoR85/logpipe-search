using System.Text.Json.Serialization;

namespace LogPipe.Search.Storage;

public sealed class LogDocument
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = "";

    [JsonPropertyName("service")]
    public string Service { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("span_id")]
    public string? SpanId { get; init; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; init; }
}
