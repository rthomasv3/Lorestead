using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.DataAccess
{
    // All lookups are by SHA-256 hash of the presented value - raw codes and
    // tokens never touch the database.
    public sealed class OAuthGrantRepository
    {
        private readonly ConnectionManager _connectionManager;

        public OAuthGrantRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public void InsertCode(string codeHash, string codeChallenge, string redirectUri, long expiresAt)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO oauth_code (code_hash, code_challenge, redirect_uri, expires_at)
                VALUES (@codeHash, @codeChallenge, @redirectUri, @expiresAt)";
            insert.Parameters.AddWithValue("@codeHash", codeHash);
            insert.Parameters.AddWithValue("@codeChallenge", codeChallenge);
            insert.Parameters.AddWithValue("@redirectUri", redirectUri);
            insert.Parameters.AddWithValue("@expiresAt", expiresAt);
            insert.ExecuteNonQuery();
        }

        // Single-use by construction: the DELETE and the read are one statement, so
        // a replayed code finds nothing no matter how the requests interleave.
        public OAuthCode ConsumeCode(string codeHash)
        {
            OAuthCode code = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand consume = connection.CreateCommand();
            consume.CommandText = @"
                DELETE FROM oauth_code WHERE code_hash = @codeHash
                RETURNING code_challenge, redirect_uri, expires_at";
            consume.Parameters.AddWithValue("@codeHash", codeHash);

            using SqliteDataReader reader = consume.ExecuteReader();

            if (reader.Read())
            {
                code = new OAuthCode
                {
                    CodeChallenge = reader.GetString(0),
                    RedirectUri = reader.GetString(1),
                    ExpiresAt = reader.GetInt64(2),
                };
            }

            return code;
        }

        public void InsertGrant(string id, string accessHash, long accessExpiresAt, string refreshHash, long refreshExpiresAt, long createdAt)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO oauth_grant (id, access_hash, access_expires_at, refresh_hash, refresh_expires_at, created_at)
                VALUES (@id, @accessHash, @accessExpiresAt, @refreshHash, @refreshExpiresAt, @createdAt)";
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@accessHash", accessHash);
            insert.Parameters.AddWithValue("@accessExpiresAt", accessExpiresAt);
            insert.Parameters.AddWithValue("@refreshHash", refreshHash);
            insert.Parameters.AddWithValue("@refreshExpiresAt", refreshExpiresAt);
            insert.Parameters.AddWithValue("@createdAt", createdAt);
            insert.ExecuteNonQuery();
        }

        public bool AccessTokenIsLive(string accessHash, long now)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM oauth_grant WHERE access_hash = @accessHash AND access_expires_at > @now";
            select.Parameters.AddWithValue("@accessHash", accessHash);
            select.Parameters.AddWithValue("@now", now);
            return select.ExecuteScalar() != null;
        }

        // Rotation: the old refresh token dies in the same statement that installs
        // the new pair. Returns false when the token is unknown or expired.
        public bool RotateGrant(string refreshHash, long now, string newAccessHash, long newAccessExpiresAt, string newRefreshHash, long newRefreshExpiresAt)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand rotate = connection.CreateCommand();
            rotate.CommandText = @"
                UPDATE oauth_grant
                SET access_hash = @newAccessHash, access_expires_at = @newAccessExpiresAt,
                    refresh_hash = @newRefreshHash, refresh_expires_at = @newRefreshExpiresAt
                WHERE refresh_hash = @refreshHash AND refresh_expires_at > @now";
            rotate.Parameters.AddWithValue("@newAccessHash", newAccessHash);
            rotate.Parameters.AddWithValue("@newAccessExpiresAt", newAccessExpiresAt);
            rotate.Parameters.AddWithValue("@newRefreshHash", newRefreshHash);
            rotate.Parameters.AddWithValue("@newRefreshExpiresAt", newRefreshExpiresAt);
            rotate.Parameters.AddWithValue("@refreshHash", refreshHash);
            rotate.Parameters.AddWithValue("@now", now);
            return rotate.ExecuteNonQuery() > 0;
        }

        public void DeleteExpired(long now)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = @"
                DELETE FROM oauth_code WHERE expires_at <= @now;
                DELETE FROM oauth_grant WHERE refresh_expires_at <= @now;";
            delete.Parameters.AddWithValue("@now", now);
            delete.ExecuteNonQuery();
        }

        // Rotating the configured client secret is the revocation mechanism: when
        // the fingerprint changes, every outstanding code and grant is wiped.
        public void SyncClientFingerprint(string fingerprint)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT client_fingerprint FROM oauth_state WHERE id = 1";
            string current = select.ExecuteScalar() as string;

            if (current != fingerprint)
            {
                using SqliteCommand wipe = connection.CreateCommand();
                wipe.Transaction = transaction;
                wipe.CommandText = @"
                    DELETE FROM oauth_code;
                    DELETE FROM oauth_grant;
                    INSERT INTO oauth_state (id, client_fingerprint) VALUES (1, @fingerprint)
                    ON CONFLICT (id) DO UPDATE SET client_fingerprint = @fingerprint;";
                wipe.Parameters.AddWithValue("@fingerprint", fingerprint);
                wipe.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }
}
