using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace LogPipe.Search.Tests;

/// <summary>
/// Minimal OIDC discovery + JWKS stub. Serves /.well-known/openid-configuration and
/// /jwks, and issues real RS256 JWTs whose issuer matches the Authority the factory
/// points at — full JwtBearer discovery path, zero secrets.
/// </summary>
public sealed class OidcStub : IDisposable
{
    private const string KeyId = "logpipe-test-key";
    private readonly RSA _rsa = RSA.Create(2048);

    public WireMockServer Server { get; }
    public string Authority { get; }

    public OidcStub()
    {
        Server = WireMockServer.Start();
        Authority = Server.Url!;

        var parameters = _rsa.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!)
                }
            }
        });

        Server.Given(Request.Create().WithPath("/.well-known/openid-configuration"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    issuer = Authority,
                    jwks_uri = Authority + "/jwks"
                })));

        Server.Given(Request.Create().WithPath("/jwks"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jwks));
    }

    public string CreateToken(params string[] scopes)
    {
        var key = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer: Authority,
            audience: null,
            claims: new[]
            {
                new Claim("scope", string.Join(' ', scopes)),
                new Claim("sub", "logpipe-test")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return handler.WriteToken(token);
    }

    public void Dispose() => Server.Dispose();
}
