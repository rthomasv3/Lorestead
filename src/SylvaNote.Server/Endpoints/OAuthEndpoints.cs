using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Server.Services;

namespace SylvaNote.Server.Endpoints;

// A deliberately tiny OAuth 2.1 authorization server for the single preconfigured
// client (features/mcp.md, decisions.md): claude.ai custom connectors cannot send a
// static Authorization header, so this is the only path for Claude web and mobile.
// Hand-rolled minimal APIs, no library - four endpoints and opaque random tokens
// are the whole surface, and the OAuth JSON vocabulary is snake_case, which the
// app-wide GaldrJson camelCase policy cannot produce.
public static class OAuthEndpoints
{
    private const int CodeLifetimeSeconds = 300;
    private const int AccessLifetimeSeconds = 3600;
    private const int RefreshLifetimeSeconds = 30 * 24 * 3600;

    public static void MapOAuthEndpoints(this WebApplication app)
    {
        // Both discovery docs, each also at its RFC path-inserted variant for the
        // /mcp resource - the MCP auth spec moved between 2025 revisions and
        // clients differ in which form they fetch. Serving all four is cheap.
        app.MapGet("/.well-known/oauth-protected-resource", (ServerConfig config) => ProtectedResourceDoc(config));
        app.MapGet("/.well-known/oauth-protected-resource/mcp", (ServerConfig config) => ProtectedResourceDoc(config));
        app.MapGet("/.well-known/oauth-authorization-server", (ServerConfig config) => AuthorizationServerDoc(config));
        app.MapGet("/.well-known/oauth-authorization-server/mcp", (ServerConfig config) => AuthorizationServerDoc(config));

        app.MapGet("/authorize", (HttpContext context, ServerConfig config, OAuthGrantRepository grants) =>
            Authorize(context, config, grants));
        app.MapPost("/token", async (HttpContext context, ServerConfig config, OAuthGrantRepository grants) =>
            await Token(context, config, grants));
    }

    // Authorization Code + PKCE with no login form or consent page: an auth code is
    // worthless without the client secret at the token exchange, and PKCE pins the
    // code to the party that started the flow, so auto-approving the one
    // preconfigured client leaks nothing (the secret is the credential).
    private static IResult Authorize(HttpContext context, ServerConfig config, OAuthGrantRepository grants)
    {
        IResult result;
        IQueryCollection query = context.Request.Query;
        string clientId = query["client_id"].ToString();
        string redirectUri = query["redirect_uri"].ToString();
        string state = query["state"].ToString();

        // An unknown client or redirect target gets a plain 400, never a redirect -
        // redirecting to an unvalidated URI is the classic code-leak (RFC 6749 4.1.2.1).
        if (clientId != config.OAuthClientId || !IsAllowedRedirect(config, redirectUri))
        {
            result = Results.Text("Unknown client_id or redirect_uri.", statusCode: StatusCodes.Status400BadRequest);
        }
        else if (query["response_type"].ToString() != "code")
        {
            result = ErrorRedirect(redirectUri, "unsupported_response_type", state);
        }
        else if (string.IsNullOrEmpty(query["code_challenge"].ToString())
            || query["code_challenge_method"].ToString() != "S256")
        {
            result = ErrorRedirect(redirectUri, "invalid_request", state);
        }
        else if (HasForeignResource(query["resource"], config))
        {
            result = ErrorRedirect(redirectUri, "invalid_target", state);
        }
        else
        {
            string code = OAuthCrypto.NewToken();
            grants.InsertCode(
                OAuthCrypto.HashHex(code),
                query["code_challenge"].ToString(),
                redirectUri,
                Now() + CodeLifetimeSeconds);

            // iss lets the client confirm who answered (RFC 9207).
            string location = redirectUri
                + (redirectUri.Contains('?') ? "&" : "?")
                + "code=" + Uri.EscapeDataString(code)
                + "&iss=" + Uri.EscapeDataString(config.PublicUrl)
                + (string.IsNullOrEmpty(state) ? "" : "&state=" + Uri.EscapeDataString(state));
            result = Results.Redirect(location);
        }

        return result;
    }

    private static async Task<IResult> Token(HttpContext context, ServerConfig config, OAuthGrantRepository grants)
    {
        IResult result;
        // no-store per RFC 6749 5.1 - successful responses carry live credentials.
        context.Response.Headers.CacheControl = "no-store";
        IFormCollection form = await context.Request.ReadFormAsync();

        if (!ClientIsAuthentic(context, form, config))
        {
            // Basic in the challenge so a client_secret_basic client retries
            // correctly; a body-auth client ignores it.
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"SylvaNote\"";
            result = OAuthError(StatusCodes.Status401Unauthorized, "invalid_client");
        }
        else
        {
            string grantType = form["grant_type"].ToString();

            if (grantType == "authorization_code")
            {
                result = ExchangeCode(form, grants);
            }
            else if (grantType == "refresh_token")
            {
                result = RefreshGrant(form, grants);
            }
            else
            {
                result = OAuthError(StatusCodes.Status400BadRequest, "unsupported_grant_type");
            }
        }

        return result;
    }

    private static IResult ExchangeCode(IFormCollection form, OAuthGrantRepository grants)
    {
        IResult result = OAuthError(StatusCodes.Status400BadRequest, "invalid_grant");
        string presented = form["code"].ToString();
        string verifier = form["code_verifier"].ToString();
        OAuthCode code = string.IsNullOrEmpty(presented) ? null : grants.ConsumeCode(OAuthCrypto.HashHex(presented));

        // The code is already consumed whatever happens next - a failed exchange
        // burns it, so nothing learned here can be retried against the same code.
        if (code != null
            && code.ExpiresAt > Now()
            && form["redirect_uri"].ToString() == code.RedirectUri
            && !string.IsNullOrEmpty(verifier)
            && OAuthCrypto.VerifierMatches(verifier, code.CodeChallenge))
        {
            string accessToken = OAuthCrypto.NewToken();
            string refreshToken = OAuthCrypto.NewToken();
            long now = Now();
            grants.InsertGrant(
                Guid.CreateVersion7().ToString(),
                OAuthCrypto.HashHex(accessToken), now + AccessLifetimeSeconds,
                OAuthCrypto.HashHex(refreshToken), now + RefreshLifetimeSeconds,
                now);
            result = TokenResponse(accessToken, refreshToken);
        }

        return result;
    }

    private static IResult RefreshGrant(IFormCollection form, OAuthGrantRepository grants)
    {
        IResult result = OAuthError(StatusCodes.Status400BadRequest, "invalid_grant");
        string presented = form["refresh_token"].ToString();

        if (!string.IsNullOrEmpty(presented))
        {
            string accessToken = OAuthCrypto.NewToken();
            string refreshToken = OAuthCrypto.NewToken();
            long now = Now();
            bool rotated = grants.RotateGrant(
                OAuthCrypto.HashHex(presented), now,
                OAuthCrypto.HashHex(accessToken), now + AccessLifetimeSeconds,
                OAuthCrypto.HashHex(refreshToken), now + RefreshLifetimeSeconds);

            if (rotated)
            {
                result = TokenResponse(accessToken, refreshToken);
            }
        }

        return result;
    }

    // client_secret_basic and client_secret_post both accepted - the task's spike
    // question is which one Claude uses, so until that answer lands, both work.
    private static bool ClientIsAuthentic(HttpContext context, IFormCollection form, ServerConfig config)
    {
        string clientId = form["client_id"].ToString();
        string clientSecret = form["client_secret"].ToString();
        string header = context.Request.Headers.Authorization.ToString();

        if (header.StartsWith("Basic ", StringComparison.Ordinal))
        {
            try
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring("Basic ".Length)));
                int separator = decoded.IndexOf(':');

                if (separator >= 0)
                {
                    // RFC 6749 2.3.1 form-encodes the credentials inside Basic;
                    // not every client does, so unescape after splitting.
                    clientId = Uri.UnescapeDataString(decoded.Substring(0, separator));
                    clientSecret = Uri.UnescapeDataString(decoded.Substring(separator + 1));
                }
            }
            catch (FormatException)
            {
                clientId = "";
                clientSecret = "";
            }
        }

        return clientId == config.OAuthClientId
            && CryptographicSecretEquals(clientSecret, config.OAuthClientSecret);
    }

    private static bool CryptographicSecretEquals(string provided, string expected)
    {
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided ?? ""),
            Encoding.UTF8.GetBytes(expected));
    }

    private static bool IsAllowedRedirect(ServerConfig config, string redirectUri)
    {
        bool allowed = false;

        foreach (string uri in config.OAuthRedirectUris)
        {
            if (string.Equals(uri, redirectUri, StringComparison.Ordinal))
            {
                allowed = true;
            }
        }

        return allowed;
    }

    // RFC 8707: a resource indicator, when sent, must name the one thing this
    // server protects - the canonical /mcp URL.
    private static bool HasForeignResource(StringValues resource, ServerConfig config)
    {
        string value = resource.ToString();
        return !string.IsNullOrEmpty(value) && value.TrimEnd('/') != config.PublicUrl + "/mcp";
    }

    private static IResult ErrorRedirect(string redirectUri, string error, string state)
    {
        string location = redirectUri
            + (redirectUri.Contains('?') ? "&" : "?")
            + "error=" + error
            + (string.IsNullOrEmpty(state) ? "" : "&state=" + Uri.EscapeDataString(state));
        return Results.Redirect(location);
    }

    private static IResult TokenResponse(string accessToken, string refreshToken)
    {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();

        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("access_token", accessToken);
            writer.WriteString("token_type", "Bearer");
            writer.WriteNumber("expires_in", AccessLifetimeSeconds);
            writer.WriteString("refresh_token", refreshToken);
            writer.WriteEndObject();
        }

        return Results.Bytes(buffer.WrittenSpan.ToArray(), "application/json");
    }

    private static IResult OAuthError(int statusCode, string error)
    {
        return Results.Text("{\"error\":\"" + error + "\"}", "application/json", statusCode: statusCode);
    }

    private static IResult ProtectedResourceDoc(ServerConfig config)
    {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();

        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("resource", config.PublicUrl + "/mcp");
            writer.WriteStartArray("authorization_servers");
            writer.WriteStringValue(config.PublicUrl);
            writer.WriteEndArray();
            writer.WriteStartArray("bearer_methods_supported");
            writer.WriteStringValue("header");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Results.Bytes(buffer.WrittenSpan.ToArray(), "application/json");
    }

    private static IResult AuthorizationServerDoc(ServerConfig config)
    {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();

        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("issuer", config.PublicUrl);
            writer.WriteString("authorization_endpoint", config.PublicUrl + "/authorize");
            writer.WriteString("token_endpoint", config.PublicUrl + "/token");
            writer.WriteStartArray("response_types_supported");
            writer.WriteStringValue("code");
            writer.WriteEndArray();
            writer.WriteStartArray("grant_types_supported");
            writer.WriteStringValue("authorization_code");
            writer.WriteStringValue("refresh_token");
            writer.WriteEndArray();
            writer.WriteStartArray("code_challenge_methods_supported");
            writer.WriteStringValue("S256");
            writer.WriteEndArray();
            writer.WriteStartArray("token_endpoint_auth_methods_supported");
            writer.WriteStringValue("client_secret_basic");
            writer.WriteStringValue("client_secret_post");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Results.Bytes(buffer.WrittenSpan.ToArray(), "application/json");
    }

    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
