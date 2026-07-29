using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SylvaNote.Core.DataAccess;

namespace SylvaNote.Server.Services;

public static class BearerAuth
{
    // The auth matrix (features/mcp.md): /mcp accepts the deployment token OR a
    // live OAuth access token; every other endpoint accepts the deployment token
    // only - an OAuth token unlocks nothing but /mcp. The OAuth endpoints
    // themselves are anonymous: discovery must be fetchable before any credential
    // exists, the browser hits /authorize bare, and /token carries its client
    // authentication in the request.
    public static IApplicationBuilder UseBearerAuth(this IApplicationBuilder app, ServerConfig config)
    {
        byte[] expected = Encoding.UTF8.GetBytes(config.Token);

        return app.Use(async (context, next) =>
        {
            PathString path = context.Request.Path;
            bool isMcp = path.StartsWithSegments("/mcp");
            bool authorized = false;

            if (config.OAuthEnabled && IsOAuthSurface(path))
            {
                authorized = true;
            }
            else
            {
                string header = context.Request.Headers.Authorization.ToString();

                if (header.StartsWith("Bearer ", StringComparison.Ordinal))
                {
                    string presented = header.Substring("Bearer ".Length);
                    authorized = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected);

                    if (!authorized && isMcp && config.OAuthEnabled)
                    {
                        OAuthGrantRepository grants = context.RequestServices.GetRequiredService<OAuthGrantRepository>();
                        authorized = grants.AccessTokenIsLive(
                            OAuthCrypto.HashHex(presented),
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    }
                }
            }

            if (authorized)
            {
                await next(context);
            }
            else
            {
                // The resource_metadata pointer is how an MCP client discovers that
                // OAuth is on offer here at all (RFC 9728).
                if (isMcp && config.OAuthEnabled)
                {
                    context.Response.Headers.WWWAuthenticate =
                        $"Bearer resource_metadata=\"{config.PublicUrl}/.well-known/oauth-protected-resource\"";
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
        });
    }

    private static bool IsOAuthSurface(PathString path)
    {
        return path.StartsWithSegments("/.well-known")
            || path.Equals("/authorize", StringComparison.Ordinal)
            || path.Equals("/token", StringComparison.Ordinal);
    }
}
