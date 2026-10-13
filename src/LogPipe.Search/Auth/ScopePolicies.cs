using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace LogPipe.Search.Auth;

public static class ScopePolicies
{
    public const string LogsRead = "LogsRead";

    public static void AddLogPipePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(LogsRead,
            policy => policy.RequireAssertion(ctx => HasScope(ctx.User, "logs:read")));
    }

    public static bool HasScope(ClaimsPrincipal principal, string scope)
    {
        var scopes = principal.FindAll("scope")
            .Concat(principal.FindAll("scp"))
            .SelectMany(c => c.Value.Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return scopes.Contains(scope, StringComparer.Ordinal);
    }
}
