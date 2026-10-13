using System.Text.Json;

namespace LogPipe.Search.Tests;

public sealed class KestrelEndpointConfigTests
{
    private static JsonElement LoadAppSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "LogPipe.Search", "appsettings.json");
            if (File.Exists(candidate))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                return doc.RootElement.Clone();
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "src/LogPipe.Search/appsettings.json not found walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Cleartext_http_endpoint_is_http1_only_not_dual_mode()
    {
        var cfg = LoadAppSettings();
        var protocols = cfg
            .GetProperty("Kestrel").GetProperty("Endpoints")
            .GetProperty("http").GetProperty("Protocols").GetString();

        Assert.Equal("Http1", protocols);
    }

    [Fact]
    public void Grpc_endpoint_is_dedicated_http2_port()
    {
        var cfg = LoadAppSettings();
        var grpc = cfg
            .GetProperty("Kestrel").GetProperty("Endpoints")
            .GetProperty("grpc");

        Assert.Equal("Http2", grpc.GetProperty("Protocols").GetString());
        Assert.Equal("http://localhost:8093", grpc.GetProperty("Url").GetString());
    }
}
