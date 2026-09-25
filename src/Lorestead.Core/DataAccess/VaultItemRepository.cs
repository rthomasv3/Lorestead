using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.Entities;
using Lorestead.Core.Ordering;
using Lorestead.Core.Sync;

namespace Lorestead.Core.DataAccess
{
    /// <summary>
    /// Vault notes: the same tree, tombstone, and purge behavior as notes over ciphertext columns.
    /// </summary>
    public sealed class VaultItemRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public VaultItemRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
        }

        public void Save(VaultItem item)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(item.CreatedAt))
            {
                item.CreatedAt = now;
            }
            item.UpdatedAt = now;

            UpsertWithin(connection, transaction, item);
            AppendChangeWithin(connection, transaction, item, now);

            transaction.Commit();
        }

        public VaultItem Get(string id)
        {
            VaultItem item = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                item = ReadItem(reader, includeBody: true);
            }
            return item;
        }

        /// <summary>
        /// Every item without its body, which is what building the unlocked index needs.
        /// </summary>
        public List<VaultItem> GetAllWithoutBodies()
        {
            List<VaultItem> items = new List<VaultItem>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectWithoutBodySql + " ORDER BY position";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadItem(reader, includeBody: false));
            }
            return items;
        }

        public string GetMaxChildPosition(string parentId)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = parentId == null
                ? "SELECT MAX(position) FROM vault_item WHERE parent_id IS NULL"
                : "SELECT MAX(position) FROM vault_item WHERE parent_id = @parent_id";
            if (parentId != null)
            {
                select.Parameters.AddWithValue("@parent_id", parentId);
            }
            object result = select.ExecuteScalar();
            return result is string value ? value : null;
        }

        public string GetNextChildPosition(string parentId, string afterPosition)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = parentId == null
                ? "SELECT MIN(position) FROM vault_item WHERE parent_id IS NULL AND position > @after"
                : "SELECT MIN(position) FROM vault_item WHERE parent_id = @parent_id AND position > @after";
            if (parentId != null)
            {
                select.Parameters.AddWithValue("@parent_id", parentId);
            }
            select.Parameters.AddWithValue("@after", afterPosition);
            object result = select.ExecuteScalar();
            return result is string value ? value : null;
        }

        public bool ChildPositionExists(string parentId, string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = parentId == null
                ? "SELECT 1 FROM vault_item WHERE parent_id IS NULL AND position = @position LIMIT 1"
                : "SELECT 1 FROM vault_item WHERE parent_id = @parent_id AND position = @position LIMIT 1";
            if (parentId != null)
            {
                select.Parameters.AddWithValue("@parent_id", parentId);
            }
            select.Parameters.AddWithValue("@position", position);
            return select.ExecuteScalar() != null;
        }

        public void TrashSubtree(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            foreach (VaultItem item in ReadSubtreeWithin(connection, transaction, id))
            {
                if (!item.Deleted)
                {
                    item.Deleted = true;
                    item.UpdatedAt = now;
                    UpsertWithin(connection, transaction, item);
                    AppendChangeWithin(connection, transaction, item, now);
                }
            }

            transaction.Commit();
        }

        public void RestoreSubtree(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            RestoreSubtreeWithin(connection, transaction, id, null, null, false);

            transaction.Commit();
        }

        public void RestoreSubtreeAt(string id, string parentId, string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            RestoreSubtreeWithin(connection, transaction, id, parentId, position, true);

            transaction.Commit();
        }

        public void PurgeSubtree(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            PurgeSubtreeWithin(connection, transaction, id);

            transaction.Commit();
        }

        public void PurgeTrash()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            foreach (string itemId in ReadIdsWithin(connection, "SELECT id FROM vault_item WHERE deleted = 1", null))
            {
                PurgeSubtreeWithin(connection, transaction, itemId);
            }

            transaction.Commit();
        }

        public void PurgeExpiredTrash(string cutoffIso)
        {
            List<string> expired;
            using (SqliteConnection connection = _connectionManager.CreateConnection())
            {
                expired = ReadIdsWithin(connection, "SELECT id FROM vault_item WHERE deleted = 1 AND updated_at < @cutoff", cutoffIso);
            }

            foreach (string itemId in expired)
            {
                PurgeSubtree(itemId);
            }
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, VaultItem item)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO vault_item (id, vault_id, parent_id, position, deleted, key_version, title_enc, body_enc, links_enc, created_at, updated_at)
                VALUES (@id, @vault_id, @parent_id, @position, @deleted, @key_version, @title_enc, @body_enc, @links_enc, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    vault_id = excluded.vault_id, parent_id = excluded.parent_id, position = excluded.position,
                    deleted = excluded.deleted, key_version = excluded.key_version, title_enc = excluded.title_enc,
                    body_enc = excluded.body_enc, links_enc = excluded.links_enc,
                    created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", item.Id);
            upsert.Parameters.AddWithValue("@vault_id", item.VaultId);
            upsert.Parameters.AddWithValue("@parent_id", (object)item.ParentId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@position", item.Position);
            upsert.Parameters.AddWithValue("@deleted", item.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@key_version", item.KeyVersion);
            upsert.Parameters.AddWithValue("@title_enc", item.TitleEnc);
            upsert.Parameters.AddWithValue("@body_enc", item.BodyEnc);
            upsert.Parameters.AddWithValue("@links_enc", item.LinksEnc);
            upsert.Parameters.AddWithValue("@created_at", item.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", item.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM vault_item WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private void RestoreSubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string id, string parentId, string position, bool placeExplicitly)
        {
            string now = Timestamps.UtcNowIso();
            foreach (VaultItem item in ReadSubtreeWithin(connection, transaction, id))
            {
                if (item.Deleted)
                {
                    item.Deleted = false;
                    item.UpdatedAt = now;

                    if (item.Id == id)
                    {
                        if (placeExplicitly)
                        {
                            item.ParentId = parentId;
                            item.Position = position;
                        }
                        else if (item.ParentId != null)
                        {
                            VaultItem parent = GetWithin(connection, transaction, item.ParentId);
                            if (parent == null || parent.Deleted)
                            {
                                item.ParentId = null;
                                item.Position = NextRootPositionWithin(connection, transaction);
                            }
                        }
                    }

                    UpsertWithin(connection, transaction, item);
                    AppendChangeWithin(connection, transaction, item, now);
                }
            }
        }

        private void PurgeSubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            string now = Timestamps.UtcNowIso();
            List<VaultItem> subtree = ReadSubtreeWithin(connection, transaction, id);

            foreach (VaultItem item in subtree)
            {
                List<string> attachmentIds = ReadIdsWithin(connection, "SELECT id FROM vault_attachment WHERE item_id = @item_id", item.Id, "@item_id");
                foreach (string attachmentId in attachmentIds)
                {
                    ChangeLogRepository.DeleteForItemWithin(connection, transaction, ItemTypes.VaultAttachment, attachmentId);
                    ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
                    {
                        ItemType = ItemTypes.VaultAttachment,
                        ItemId = attachmentId,
                        Op = ChangeOps.Purge,
                        DeviceId = _deviceId,
                        ChangedAt = now,
                    });
                }
            }

            foreach (VaultItem item in subtree)
            {
                DeleteRowWithin(connection, transaction, item.Id);
                ChangeLogRepository.DeleteForItemWithin(connection, transaction, ItemTypes.VaultItem, item.Id);
                ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.VaultItem,
                    ItemId = item.Id,
                    Op = ChangeOps.Purge,
                    DeviceId = _deviceId,
                    ChangedAt = now,
                });
            }
        }

        private void AppendChangeWithin(SqliteConnection connection, SqliteTransaction transaction, VaultItem item, string now)
        {
            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.VaultItem,
                ItemId = item.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(item),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.VaultItem, item.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);
        }

        private static VaultItem GetWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            VaultItem item = null;
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                item = ReadItem(reader, includeBody: true);
            }
            return item;
        }

        private static List<VaultItem> ReadSubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string rootId)
        {
            List<VaultItem> items = new List<VaultItem>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = $@"
                WITH RECURSIVE sub (id) AS (
                    SELECT id FROM vault_item WHERE id = @id
                    UNION ALL
                    SELECT i.id FROM vault_item i JOIN sub ON i.parent_id = sub.id
                )
                {SelectSql} WHERE id IN (SELECT id FROM sub)";
            select.Parameters.AddWithValue("@id", rootId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadItem(reader, includeBody: true));
            }
            return items;
        }

        private static List<string> ReadIdsWithin(SqliteConnection connection, string sql, string parameterValue, string parameterName = "@cutoff")
        {
            List<string> ids = new List<string>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = sql;
            if (parameterValue != null)
            {
                select.Parameters.AddWithValue(parameterName, parameterValue);
            }
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
            return ids;
        }

        private static string NextRootPositionWithin(SqliteConnection connection, SqliteTransaction transaction)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(position) FROM vault_item WHERE parent_id IS NULL AND deleted = 0";
            object result = select.ExecuteScalar();
            string last = result is string value ? value : null;
            return FractionalIndex.Between(last, null);
        }

        private const string SelectSql =
            "SELECT id, vault_id, parent_id, position, deleted, key_version, title_enc, links_enc, created_at, updated_at, body_enc FROM vault_item";

        private const string SelectWithoutBodySql =
            "SELECT id, vault_id, parent_id, position, deleted, key_version, title_enc, links_enc, created_at, updated_at FROM vault_item";

        private static VaultItem ReadItem(SqliteDataReader reader, bool includeBody)
        {
            return new VaultItem
            {
                Id = reader.GetString(0),
                VaultId = reader.GetString(1),
                ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                Position = reader.GetString(3),
                Deleted = reader.GetInt64(4) != 0,
                KeyVersion = reader.GetInt32(5),
                TitleEnc = (byte[])reader.GetValue(6),
                LinksEnc = (byte[])reader.GetValue(7),
                CreatedAt = reader.GetString(8),
                UpdatedAt = reader.GetString(9),
                BodyEnc = includeBody ? (byte[])reader.GetValue(10) : null,
            };
        }
    }
}
