using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.QueryDsl;
using LogPipe.Contracts.Validation;
using LogPipe.Search.Auth;
using LogPipe.Search.Storage;
using Microsoft.AspNetCore.Authorization;

namespace LogPipe.Search.Api;

public sealed record FacetBucket(string Value, long Count);
public sealed record SearchFacets(IReadOnlyList<FacetBucket> Levels, IReadOnlyList<FacetBucket> Services);
public sealed record SearchHit(string Id, DateTimeOffset Timestamp, string Level, string Service,
    string Message, string? TraceId, string? SpanId, Dictionary<string, string>? Attributes);
public sealed record SearchResponse(long Total, IReadOnlyList<SearchHit> Hits, SearchFacets Facets);
public sealed record TraceResponse(string TraceId, IReadOnlyList<SearchHit> Entries);

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/logs", SearchAsync)
            .RequireAuthorization(ScopePolicies.LogsRead)
            .WithName("SearchLogs")
            .Produces<SearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        app.MapGet("/v1/logs/trace/{traceId}", TraceAsync)
            .RequireAuthorization(ScopePolicies.LogsRead)
            .WithName("SearchTrace")
            .Produces<TraceResponse>(StatusCodes.Status200OK);
        return app;
    }

    private static async Task<IResult> SearchAsync(
        string? q, string? level, string? service, int? page, int? size,
        ElasticsearchClient client, CancellationToken ct)
    {
        if (level is not null && !LogEntryRules.Levels.Contains(level))
        {
            return Results.Problem($"unknown level '{level}'",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var pageNumber = Math.Max(page ?? 1, 1);
        var pageSize = Math.Clamp(size ?? 20, 1, 100);
        var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var response = await client.SearchAsync<LogDocument>(search => search
            .Indices(IndexNaming.ReadPattern)
            .From((pageNumber - 1) * pageSize)
            .Size(pageSize)
            .Sort(sort => sort.Field(new Field("timestamp"), SortOrder.Desc))
            .Query(query => query.Bool(b =>
            {
                if (term is not null)
                    b.Must(m => m.Fuzzy(f => f.Field("message").Value(term).Fuzziness("auto")));
                // Filter() replaces on each call — pass every filter in a single call.
                var filters = new List<Action<QueryDescriptor<LogDocument>>>();
                if (level is not null) filters.Add(f => f.Term(t => t.Field("level").Value(level)));
                if (service is not null) filters.Add(f => f.Term(t => t.Field("service").Value(service)));
                if (filters.Count > 0) b.Filter(filters.ToArray());
            }))
            .Aggregations(aggs => aggs
                .Add("levels", t => t.Terms(tt => tt.Field("level").Size(LogEntryRules.Levels.Count)))
                .Add("services", t => t.Terms(tt => tt.Field("service").Size(20)))), ct);

        var hits = response.Hits.Select(hit => ToHit(hit.Source!, hit.Id)).ToList();
        var facets = new SearchFacets(
            ToBuckets(response.Aggregations, "levels"),
            ToBuckets(response.Aggregations, "services"));
        var total = response.HitsMetadata is null
            ? 0
            : response.HitsMetadata.Total.Match(t => t.Value, l => l);
        return Results.Ok(new SearchResponse(total, hits, facets));
    }

    private static async Task<IResult> TraceAsync(
        string traceId, ElasticsearchClient client, CancellationToken ct)
    {
        var response = await client.SearchAsync<LogDocument>(search => search
            .Indices(IndexNaming.ReadPattern)
            .Size(1000)
            .Sort(sort => sort.Field(new Field("timestamp"), SortOrder.Asc))
            .Query(q => q.Term(t => t.Field("trace_id").Value(traceId))), ct);

        var entries = response.Hits.Select(hit => ToHit(hit.Source!, hit.Id)).ToList();
        return Results.Ok(new TraceResponse(traceId, entries));
    }

    private static SearchHit ToHit(LogDocument doc, string id) => new(
        doc.Id.Length > 0 ? doc.Id : id, doc.Timestamp, doc.Level, doc.Service, doc.Message,
        doc.TraceId, doc.SpanId, doc.Attributes);

    private static IReadOnlyList<FacetBucket> ToBuckets(AggregateDictionary? aggregations, string name)
    {
        if (aggregations is null) return [];
        if (!aggregations.TryGetAggregate<StringTermsAggregate>(name, out var terms) || terms is null)
            return [];
        return terms.Buckets
            .Select(bucket => new FacetBucket(bucket.Key.ToString() ?? "", bucket.DocCount))
            .ToList();
    }
}
