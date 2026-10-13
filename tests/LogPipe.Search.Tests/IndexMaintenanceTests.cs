using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;

namespace LogPipe.Search.Tests;

[Collection("containers")]
public sealed class IndexMaintenanceTests
{
    private readonly ContainersFixture _fixture;
    public IndexMaintenanceTests(ContainersFixture fixture) => _fixture = fixture;

    private static ElasticsearchClient NewClient(string url) =>
        new(new ElasticsearchClientSettings(new Uri(url)));

    [Fact]
    public async Task Old_indices_are_deleted_after_retention()
    {
        using var factory = _fixture.CreateFactory("http://localhost:8090", retentionDays: 7);
        factory.CreateClient(); // host start: template applied + maintenance first run
        var es = NewClient(_fixture.ElasticsearchUrl);
        var oldIndex = "logs-2000.01.01";
        await es.Indices.CreateAsync(oldIndex);

        var maintenance = factory.Services.GetRequiredService<Storage.IndexMaintenance>();
        await maintenance.RunOnceAsync(CancellationToken.None);

        var exists = await es.Indices.ExistsAsync(oldIndex);
        Assert.False(exists.Exists);
    }

    [Fact]
    public async Task Logs_current_alias_points_to_today()
    {
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        factory.CreateClient();
        var maintenance = factory.Services.GetRequiredService<Storage.IndexMaintenance>();
        await maintenance.RunOnceAsync(CancellationToken.None);

        var es = NewClient(_fixture.ElasticsearchUrl);
        var alias = await es.Indices.GetAliasAsync((Names)Storage.IndexNaming.CurrentAlias);
        var today = Storage.IndexNaming.IndexName(DateTimeOffset.UtcNow);
        Assert.Contains(today, alias.Aliases.Keys);
    }
}
