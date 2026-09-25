using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    /// <summary>
    /// End-to-end encrypted vault: structure in clear columns, content as per-field ciphertext.
    /// </summary>
    public sealed class Db011_Vault : IMigration
    {
        public int Version => 11;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE vault (
                    id          TEXT    NOT NULL PRIMARY KEY,
                    key_version INTEGER NOT NULL DEFAULT 1,
                    created_at  TEXT    NOT NULL,
                    updated_at  TEXT    NOT NULL
                );

                CREATE TABLE vault_key (
                    id              TEXT    NOT NULL PRIMARY KEY,
                    vault_id        TEXT    NOT NULL REFERENCES vault (id) ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED,
                    kind            INTEGER NOT NULL,
                    wrapped_key     BLOB    NOT NULL,
                    kdf_salt        BLOB,
                    kdf_memory      INTEGER,
                    kdf_iterations  INTEGER,
                    kdf_parallelism INTEGER,
                    created_at      TEXT    NOT NULL,
                    updated_at      TEXT    NOT NULL
                );
                CREATE INDEX idx_vault_key_vault ON vault_key (vault_id);

                CREATE TABLE vault_item (
                    id          TEXT    NOT NULL PRIMARY KEY,
                    vault_id    TEXT    NOT NULL REFERENCES vault (id) DEFERRABLE INITIALLY DEFERRED,
                    parent_id   TEXT    REFERENCES vault_item (id) DEFERRABLE INITIALLY DEFERRED,
                    position    TEXT    NOT NULL,
                    deleted     INTEGER NOT NULL DEFAULT 0,
                    key_version INTEGER NOT NULL,
                    title_enc   BLOB    NOT NULL,
                    body_enc    BLOB    NOT NULL,
                    links_enc   BLOB    NOT NULL,
                    created_at  TEXT    NOT NULL,
                    updated_at  TEXT    NOT NULL
                );
                CREATE INDEX idx_vault_item_parent ON vault_item (parent_id);

                CREATE TABLE vault_attachment (
                    id          TEXT    NOT NULL PRIMARY KEY,
                    item_id     TEXT    NOT NULL REFERENCES vault_item (id) ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED,
                    size_bytes  INTEGER NOT NULL DEFAULT 0,
                    deleted     INTEGER NOT NULL DEFAULT 0,
                    key_version INTEGER NOT NULL,
                    name_enc    BLOB    NOT NULL,
                    mime_enc    BLOB    NOT NULL,
                    created_at  TEXT    NOT NULL,
                    updated_at  TEXT    NOT NULL
                );
                CREATE INDEX idx_vault_attachment_item ON vault_attachment (item_id);

                CREATE TABLE vault_blob (
                    attachment_id TEXT NOT NULL PRIMARY KEY REFERENCES vault_attachment (id) ON DELETE CASCADE,
                    data          BLOB NOT NULL
                );";
            command.ExecuteNonQuery();
        }
    }
}
