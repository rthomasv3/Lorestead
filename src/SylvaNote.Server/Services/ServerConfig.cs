using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SylvaNote.Server.Services;

public sealed class ServerConfig
{
    // Where claude.ai sends the browser back after the authorize redirect. Both
    // Anthropic domains, overridable for other OAuth-speaking agent hosts.
    private static readonly string[] DefaultRedirectUris =
    {
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
    };

    public string Token { get; set; }
    public string DbFilePath { get; set; }
    public string DbKey { get; set; }
    // data.md: last 50 versions per item, configurable 10-100.
    public int HistoryRetention { get; set; } = 50;
    // How long purge entries stay replayable; a device offline longer full-resyncs.
    public int PurgeRetentionDays { get; set; } = 90;
    // The externally visible base URL (issuer) - what discovery docs and the
    // WWW-Authenticate pointer are built from. Set explicitly rather than derived
    // from the request, because behind a reverse proxy the request lies.
    public string PublicUrl { get; set; }
    public string OAuthClientId { get; set; }
    public string OAuthClientSecret { get; set; }
    public IReadOnlyList<string> OAuthRedirectUris { get; set; } = DefaultRedirectUris;

    public bool OAuthEnabled
    {
        get { return !string.IsNullOrWhiteSpace(OAuthClientId) && !string.IsNullOrWhiteSpace(OAuthClientSecret); }
    }

    public static ServerConfig FromEnvironment()
    {
        string token = Environment.GetEnvironmentVariable("SYLVANOTE_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("SYLVANOTE_TOKEN environment variable is required.");
        }

        // Required, never optional: a misconfigured deployment must fail loudly, not
        // silently run unencrypted (decisions.md - encryption tier 1).
        string dbKey = Environment.GetEnvironmentVariable("SYLVANOTE_DB_KEY");

        if (string.IsNullOrWhiteSpace(dbKey))
        {
            throw new InvalidOperationException("SYLVANOTE_DB_KEY environment variable is required.");
        }

        string dataDir = Environment.GetEnvironmentVariable("SYLVANOTE_DATA_DIR");

        if (string.IsNullOrWhiteSpace(dataDir))
        {
            dataDir = "data";
        }

        string dbFilePath = Path.Combine(dataDir, "sylvanote.db");
        EnsureWritable(dataDir, dbFilePath);

        ServerConfig config = new ServerConfig
        {
            Token = token,
            DbFilePath = dbFilePath,
            DbKey = dbKey,
            HistoryRetention = ReadClampedInt("SYLVANOTE_HISTORY_RETENTION", 50, 10, 100),
            PurgeRetentionDays = ReadClampedInt("SYLVANOTE_PURGE_RETENTION_DAYS", 90, 1, 3650),
        };

        ReadOAuth(config);

        return config;
    }

    private static void ReadOAuth(ServerConfig config)
    {
        string clientId = Environment.GetEnvironmentVariable("SYLVANOTE_OAUTH_CLIENT_ID");
        string clientSecret = Environment.GetEnvironmentVariable("SYLVANOTE_OAUTH_CLIENT_SECRET");

        // Half a client is a misconfiguration, not a disabled feature - fail loudly
        // like the other required variables do.
        if (string.IsNullOrWhiteSpace(clientId) != string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "SYLVANOTE_OAUTH_CLIENT_ID and SYLVANOTE_OAUTH_CLIENT_SECRET must be set together.");
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            string publicUrl = Environment.GetEnvironmentVariable("SYLVANOTE_PUBLIC_URL");

            if (string.IsNullOrWhiteSpace(publicUrl))
            {
                throw new InvalidOperationException(
                    "SYLVANOTE_PUBLIC_URL is required when OAuth is configured - discovery documents " +
                    "must name the externally visible URL, which a proxied request cannot supply.");
            }

            config.OAuthClientId = clientId.Trim();
            config.OAuthClientSecret = clientSecret.Trim();
            config.PublicUrl = publicUrl.Trim().TrimEnd('/');

            string redirectUris = Environment.GetEnvironmentVariable("SYLVANOTE_OAUTH_REDIRECT_URIS");

            if (!string.IsNullOrWhiteSpace(redirectUris))
            {
                config.OAuthRedirectUris = redirectUris
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
            }
        }
    }

    // A denied write here is almost always bind-mount ownership: the directory
    // still owned by the host user, or a database created by an older container
    // that ran as root. Name the fix instead of letting SQLite throw a raw error.
    private static void EnsureWritable(string dataDir, string dbFilePath)
    {
        string probePath = Path.Combine(dataDir, ".write-probe");

        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllBytes(probePath, Array.Empty<byte>());
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            throw new InvalidOperationException(
                $"The data directory '{Path.GetFullPath(dataDir)}' is not writable by this process. " +
                "The container runs as non-root uid 1654 - if the directory is a bind mount, make it " +
                "writable on the host: chown -R 1654:1654 <directory>.",
                ex);
        }

        // OpenOrCreate keeps this probe harmless: a missing database becomes an
        // empty file, which SQLite treats as a fresh database anyway.
        try
        {
            using FileStream probe = File.Open(dbFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            throw new InvalidOperationException(
                $"The database file '{Path.GetFullPath(dbFilePath)}' exists but is not writable by this " +
                "process (non-root uid 1654) - it was likely created by an earlier container that ran as " +
                "root. Fix the ownership on the host: chown -R 1654:1654 <data directory>.",
                ex);
        }
    }

    private static int ReadClampedInt(string variable, int fallback, int min, int max)
    {
        string raw = Environment.GetEnvironmentVariable(variable);
        int value = fallback;

        if (int.TryParse(raw, out int parsed))
        {
            value = Math.Clamp(parsed, min, max);
        }

        return value;
    }
}
