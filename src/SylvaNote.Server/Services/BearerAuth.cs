using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SylvaNote.Server.Services;

public static class BearerAuth
{
    // The whole API sits behind the single deploy-time token (decisions.md),
    // including /status - the Settings badge doubles as a token check.
    public static IApplicationBuilder UseBearerAuth(this IApplicationBuilder app, ServerConfig config)
    {
        byte[] expected = Encoding.UTF8.GetBytes(config.Token);

        return app.Use(async (context, next) =>
        {
            bool authorized = false;
            string header = context.Request.Headers.Authorization.ToString();

            if (header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                byte[] provided = Encoding.UTF8.GetBytes(header.Substring("Bearer ".Length));
                authorized = CryptographicOperations.FixedTimeEquals(provided, expected);
            }

            if (authorized)
            {
                await next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
        });
    }
}
