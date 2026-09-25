using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;

namespace Lorestead.Core.DataAccess
{
    public sealed class VaultAttachmentRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public VaultAttachmentRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
        }

        public void Save(VaultAttachment attachment)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(attachment.CreatedAt))
            {
                attachment.CreatedAt = now;
            }
            attachment.UpdatedAt = now;

            UpsertWithin(connection, transaction, attachment);

            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.VaultAttachment,
                ItemId = attachment.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(attachment),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.VaultAttachment, attachment.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);

            transaction.Commit();
        }

        public VaultAttachment Get(string id)
        {
            VaultAttachment attachment = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                attachment = ReadAttachment(reader);
            }
            return attachment;
        }

        public List<VaultAttachment> GetForItem(string itemId)
        {
            List<VaultAttachment> attachments = new List<VaultAttachment>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE item_id = @item_id AND deleted = 0 ORDER BY created_at";
            select.Parameters.AddWithValue("@item_id", itemId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                attachments.Add(ReadAttachment(reader));
            }
            return attachments;
        }

        public List<VaultAttachment> GetAll()
        {
            List<VaultAttachment> attachments = new List<VaultAttachment>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE deleted = 0 ORDER BY created_at";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                attachments.Add(ReadAttachment(reader));
            }
            return attachments;
        }

        public void SaveBlob(string attachmentId, byte[] data)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO vault_blob (attachment_id, data) VALUES (@id, @data)";
            insert.Parameters.AddWithValue("@id", attachmentId);
            insert.Parameters.AddWithValue("@data", data);
            insert.ExecuteNonQuery();
        }

        public byte[] GetBlob(string attachmentId)
        {
            byte[] data = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT data FROM vault_blob WHERE attachment_id = @id";
            select.Parameters.AddWithValue("@id", attachmentId);
            object result = select.ExecuteScalar();
            if (result is byte[] bytes)
            {
                data = bytes;
            }
            return data;
        }

        public List<string> GetIdsMissingBlob()
        {
            List<string> ids = new List<string>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT a.id FROM vault_attachment a
                LEFT JOIN vault_blob b ON b.attachment_id = a.id
                WHERE b.attachment_id IS NULL AND a.deleted = 0";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
            return ids;
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, VaultAttachment attachment)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO vault_attachment (id, item_id, size_bytes, deleted, key_version, name_enc, mime_enc, created_at, updated_at)
                VALUES (@id, @item_id, @size_bytes, @deleted, @key_version, @name_enc, @mime_enc, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    item_id = excluded.item_id, size_bytes = excluded.size_bytes, deleted = excluded.deleted,
                    key_version = excluded.key_version, name_enc = excluded.name_enc, mime_enc = excluded.mime_enc,
                    created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", attachment.Id);
            upsert.Parameters.AddWithValue("@item_id", attachment.ItemId);
            upsert.Parameters.AddWithValue("@size_bytes", attachment.SizeBytes);
            upsert.Parameters.AddWithValue("@deleted", attachment.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@key_version", attachment.KeyVersion);
            upsert.Parameters.AddWithValue("@name_enc", attachment.NameEnc);
            upsert.Parameters.AddWithValue("@mime_enc", attachment.MimeEnc);
            upsert.Parameters.AddWithValue("@created_at", attachment.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", attachment.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM vault_attachment WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private const string SelectSql =
            "SELECT id, item_id, size_bytes, deleted, key_version, name_enc, mime_enc, created_at, updated_at FROM vault_attachment";

        private static VaultAttachment ReadAttachment(SqliteDataReader reader)
        {
            return new VaultAttachment
            {
                Id = reader.GetString(0),
                ItemId = reader.GetString(1),
                SizeBytes = reader.GetInt64(2),
                Deleted = reader.GetInt64(3) != 0,
                KeyVersion = reader.GetInt32(4),
                NameEnc = (byte[])reader.GetValue(5),
                MimeEnc = (byte[])reader.GetValue(6),
                CreatedAt = reader.GetString(7),
                UpdatedAt = reader.GetString(8),
            };
        }
    }
}
