using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Grpc.Core;
using LogPipe.Contracts.V1;
using LogPipe.Search.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LogPipe.Search.Tests;

[Collection("containers")]
public sealed class SearchApiTests : IDisposable
{
    private readonly ContainersFixture _fixture;
    private readonly OidcStub _oidc = new();
    private readonly string _service = $"searchapi-{Guid.NewGuid():N}";

    public SearchApiTests(ContainersFixture fixture) => _fixture = fixture;

    public void Dispose() => _oidc.Dispose();

    private async Task SeedAsync(WebApplicationFactory<Program> factory, int count = 1,
        string level = "Information", string? traceId = null)
    {
        factory.CreateClient();
        var stub = new LogIngest.LogIngestClient(ContainersFixture.CreateGrpcChannel(factory));
        using var call = stub.StreamLogs();
        for (var i = 0; i < count; i++)
        {
            await call.RequestStream.WriteAsync(new LogEntry
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Level = level,
                Service = _service,
                Message = i == 0 ? "connection refused by upstream" : $"bulk entry {i}",
                TraceId = traceId ?? "",
                SpanId = ""
            });
        }
        await call.RequestStream.CompleteAsync();
        await call.ResponseAsync;
        await Task.Delay(1200); // near-real-time refresh window
    }

    [Fact]
    public async Task Missing_token_returns_401()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/logs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Writer_token_cannot_read_returns_403()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:write"));
        var response = await client.GetAsync("/v1/logs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reader_token_with_no_data_returns_200_empty()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));
        var response = await client.GetAsync($"/v1/logs?service={_service}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Equal(0, payload!.Total);
        Assert.Empty(payload.Hits);
    }

    [Fact]
    public async Task Seeded_entries_return_hits_and_facets()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        await SeedAsync(factory, count: 1, level: "Information");
        await SeedAsync(factory, count: 1, level: "Error");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));

        var response = await client.GetAsync($"/v1/logs?service={_service}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Equal(2, payload!.Total);
        Assert.Contains(payload.Facets.Levels, f => f.Value == "Information" && f.Count == 1);
        Assert.Contains(payload.Facets.Levels, f => f.Value == "Error" && f.Count == 1);
        Assert.Contains(payload.Facets.Services, f => f.Value == _service && f.Count == 2);
    }

    [Fact]
    public async Task Level_filter_returns_only_that_level()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        await SeedAsync(factory, count: 1, level: "Warning");
        await SeedAsync(factory, count: 1, level: "Debug");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));

        var response = await client.GetAsync($"/v1/logs?service={_service}&level=Warning");
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Equal(1, payload!.Total);
        Assert.All(payload.Hits, hit => Assert.Equal("Warning", hit.Level));
    }

    [Fact]
    public async Task Unknown_level_returns_400()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));
        var response = await client.GetAsync("/v1/logs?level=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fuzzy_query_matches_misspelled_word()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        await SeedAsync(factory); // message: "connection refused by upstream"
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));

        var response = await client.GetAsync($"/v1/logs?service={_service}&q=connecton");
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Equal(1, payload!.Total);
    }

    [Fact]
    public async Task Trace_endpoint_returns_entries_ascending()
    {
        var traceId = Guid.NewGuid().ToString("N")[..32]; // 32 hex chars (valid per contract)
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        await SeedAsync(factory, count: 2, traceId: traceId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));

        var response = await client.GetAsync($"/v1/logs/trace/{traceId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TraceResponse>();
        Assert.Equal(traceId, payload!.TraceId);
        Assert.Equal(2, payload.Entries.Count);
        Assert.True(payload.Entries[0].Timestamp <= payload.Entries[1].Timestamp);
    }

    [Fact]
    public async Task Paging_respects_size()
    {
        using var factory = _fixture.CreateFactory(_oidc.Authority);
        await SeedAsync(factory, count: 3);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _oidc.CreateToken("logs:read"));

        var response = await client.GetAsync($"/v1/logs?service={_service}&size=2&page=1");
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Equal(3, payload!.Total);
        Assert.Equal(2, payload.Hits.Count);
    }
}
