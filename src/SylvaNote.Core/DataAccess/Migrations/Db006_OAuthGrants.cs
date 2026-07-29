using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess.Migrations
{
    // Server-only OAuth state for the /mcp endpoint (claude.ai custom connectors).
    // Tokens and codes are stored as SHA-256 hashes: the DB never holds a value
    // that works as a live credential. oauth_state pins the configured client
    // credentials - when they change at deploy time, startup wipes every grant,
    // which is the whole revocation story (rotate secret = revoke everything).
    public sealed class Db006_OAuthGrants : IMigration
    {
        public int Version => 6;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE oauth_code (
                    code_hash      TEXT PRIMARY KEY,
                    code_challenge TEXT NOT NULL,
                    redirect_uri   TEXT NOT NULL,
                    expires_at     INTEGER NOT NULL
                );
                CREATE TABLE oauth_grant (
                    id                 TEXT PRIMARY KEY,
                    access_hash        TEXT NOT NULL UNIQUE,
                    access_expires_at  INTEGER NOT NULL,
                    refresh_hash       TEXT NOT NULL UNIQUE,
                    refresh_expires_at INTEGER NOT NULL,
                    created_at         INTEGER NOT NULL
                );
                CREATE TABLE oauth_state (
                    id                 INTEGER PRIMARY KEY CHECK (id = 1),
                    client_fingerprint TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();
        }
    }
}
