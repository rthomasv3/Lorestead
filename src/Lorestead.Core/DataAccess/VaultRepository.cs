using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;

namespace Lorestead.Core.DataAccess
{
    /// <summary>
    /// The vault row and its wrapped-key rows. There is at most one vault.
    /// </summary>
    public sealed class VaultRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public VaultRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
        }

        public Vault Get()
        {
            Vault vault = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = VaultSelectSql + " ORDER BY created_at LIMIT 1";
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                vault = ReadVault(reader);
            }
            return vault;
        }

        public void Create(Vault vault, VaultKey passwordKey, VaultKey recoveryKey)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            if (ExistsWithin(connection, transaction))
            {
                throw new InvalidOperationException("A vault already exists.");
            }

            string now = Timestamps.UtcNowIso();
            vault.CreatedAt = now;
            vault.UpdatedAt = now;
            UpsertVaultWithin(connection, transaction, vault);
            AppendVaultChangeWithin(connection, transaction, vault, now);

            foreach (VaultKey key in new[] { passwordKey, recoveryKey })
            {
                key.VaultId = vault.Id;
                key.CreatedAt = now;
                key.UpdatedAt = now;
                UpsertKeyWithin(connection, transaction, key);
                AppendKeyChangeWithin(connection, transaction, key, now);
            }

            transaction.Commit();
        }

        public List<VaultKey> GetKeys(string vaultId)
        {
            List<VaultKey> keys = new List<VaultKey>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = KeySelectSql + " WHERE vault_id = @vault_id ORDER BY kind, created_at";
            select.Parameters.AddWithValue("@vault_id", vaultId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                keys.Add(ReadKey(reader));
            }
            return keys;
        }

        public VaultKey GetKey(string vaultId, VaultKeyKind kind)
        {
            VaultKey key = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = KeySelectSql + " WHERE vault_id = @vault_id AND kind = @kind ORDER BY created_at LIMIT 1";
            select.Parameters.AddWithValue("@vault_id", vaultId);
            select.Parameters.AddWithValue("@kind", (int)kind);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                key = ReadKey(reader);
            }
            return key;
        }

        /// <summary>
        /// Replaces a key row in place: a password change or recovery regeneration rewraps under the same row id.
        /// </summary>
        public void SaveKey(VaultKey key)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(key.CreatedAt))
            {
                key.CreatedAt = now;
            }
            key.UpdatedAt = now;

            UpsertKeyWithin(connection, transaction, key);
            AppendKeyChangeWithin(connection, transaction, key, now);

            transaction.Commit();
        }

        public static void UpsertVaultWithin(SqliteConnection connection, SqliteTransaction transaction, Vault vault)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO vault (id, key_version, created_at, updated_at)
                VALUES (@id, @key_version, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    key_version = excluded.key_version, created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", vault.Id);
            upsert.Parameters.AddWithValue("@key_version", vault.KeyVersion);
            upsert.Parameters.AddWithValue("@created_at", vault.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", vault.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void UpsertKeyWithin(SqliteConnection connection, SqliteTransaction transaction, VaultKey key)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO vault_key (id, vault_id, kind, wrapped_key, kdf_salt, kdf_memory, kdf_iterations, kdf_parallelism, created_at, updated_at)
                VALUES (@id, @vault_id, @kind, @wrapped_key, @kdf_salt, @kdf_memory, @kdf_iterations, @kdf_parallelism, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    vault_id = excluded.vault_id, kind = excluded.kind, wrapped_key = excluded.wrapped_key,
                    kdf_salt = excluded.kdf_salt, kdf_memory = excluded.kdf_memory, kdf_iterations = excluded.kdf_iterations,
                    kdf_parallelism = excluded.kdf_parallelism, created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", key.Id);
            upsert.Parameters.AddWithValue("@vault_id", key.VaultId);
            upsert.Parameters.AddWithValue("@kind", (int)key.Kind);
            upsert.Parameters.AddWithValue("@wrapped_key", key.WrappedKey);
            upsert.Parameters.AddWithValue("@kdf_salt", (object)key.KdfSalt ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@kdf_memory", (object)key.KdfMemoryKiB ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@kdf_iterations", (object)key.KdfIterations ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@kdf_parallelism", (object)key.KdfParallelism ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@created_at", key.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", key.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void DeleteVaultRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM vault WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        public static void DeleteKeyRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM vault_key WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private static bool ExistsWithin(SqliteConnection connection, SqliteTransaction transaction)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM vault LIMIT 1";
            return select.ExecuteScalar() != null;
        }

        private void AppendVaultChangeWithin(SqliteConnection connection, SqliteTransaction transaction, Vault vault, string now)
        {
            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Vault,
                ItemId = vault.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(vault),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Vault, vault.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);
        }

        private void AppendKeyChangeWithin(SqliteConnection connection, SqliteTransaction transaction, VaultKey key, string now)
        {
            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.VaultKey,
                ItemId = key.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(key),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.VaultKey, key.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);
        }

        private const string VaultSelectSql = "SELECT id, key_version, created_at, updated_at FROM vault";

        private const string KeySelectSql =
            "SELECT id, vault_id, kind, wrapped_key, kdf_salt, kdf_memory, kdf_iterations, kdf_parallelism, created_at, updated_at FROM vault_key";

        private static Vault ReadVault(SqliteDataReader reader)
        {
            return new Vault
            {
                Id = reader.GetString(0),
                KeyVersion = reader.GetInt32(1),
                CreatedAt = reader.GetString(2),
                UpdatedAt = reader.GetString(3),
            };
        }

        private static VaultKey ReadKey(SqliteDataReader reader)
        {
            return new VaultKey
            {
                Id = reader.GetString(0),
                VaultId = reader.GetString(1),
                Kind = (VaultKeyKind)reader.GetInt32(2),
                WrappedKey = (byte[])reader.GetValue(3),
                KdfSalt = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4),
                KdfMemoryKiB = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                KdfIterations = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                KdfParallelism = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                CreatedAt = reader.GetString(8),
                UpdatedAt = reader.GetString(9),
            };
        }
    }
}
