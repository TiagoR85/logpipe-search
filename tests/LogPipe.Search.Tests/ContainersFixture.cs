using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace LogPipe.Search.Tests;

public sealed class ContainersFixture : IAsyncLifetime
{
    private readonly IContainer _es = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.5.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(9200, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPath("/_cluster/health").ForPort(9200)))
        .Build();

    public string ElasticsearchUrl =>
        $"http://localhost:{_es.GetMappedPublicPort(9200)}";

    public async Task InitializeAsync() => await _es.StartAsync();

    public async Task DisposeAsync() => await _es.DisposeAsync();

    public WebApplicationFactory<Program> CreateFactory(string jwtAuthority, int retentionDays = 7)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Elasticsearch:Url", ElasticsearchUrl);
            builder.UseSetting("Jwt:Authority", jwtAuthority);
            builder.UseSetting("Jwt:RequireHttpsMetadata", "false");
            builder.UseSetting("Retention:Days", retentionDays.ToString());
        });
    }

    public static GrpcChannel CreateGrpcChannel(WebApplicationFactory<Program> factory) =>
        GrpcChannel.ForAddress(factory.Server.BaseAddress!, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler()
        });
}

[CollectionDefinition("containers")]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>;
