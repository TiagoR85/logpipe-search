using System.Net;

namespace LogPipe.Search.Tests;

[Collection("containers")]
public sealed class SmokeTests
{
    private readonly ContainersFixture _fixture;
    public SmokeTests(ContainersFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ready_returns_200_when_es_connected()
    {
        using var factory = _fixture.CreateFactory("http://localhost:8090");
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Nlog_config_parses()
    {
        var cfg = new NLog.Config.XmlLoggingConfiguration(
            Path.Combine(AppContext.BaseDirectory, "nlog.config"));
        Assert.NotNull(cfg);
        Assert.NotEmpty(cfg.AllTargets);
    }
}
