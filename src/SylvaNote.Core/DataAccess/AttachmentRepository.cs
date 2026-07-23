using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;

namespace SylvaNote.Core.DataAccess
{
    public sealed class AttachmentRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;

        public AttachmentRepository(ConnectionManager connectionManager, string deviceId)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
        }

        public void Save(Attachment attachment)
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

            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Attachment,
                ItemId = attachment.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(attachment),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Attachment, attachment.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            });

            transaction.Commit();
        }

        public Attachment Get(string id)
        {
            Attachment attachment = null;
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

        // Blobs never touch the change log (data.md) — they move over dedicated endpoints.
        public void SaveBlob(string attachmentId, byte[] data)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO attachment_blob (attachment_id, data) VALUES (@id, @data)";
            insert.Parameters.AddWithValue("@id", attachmentId);
            insert.Parameters.AddWithValue("@data", data);
            insert.ExecuteNonQuery();
        }

        public byte[] GetBlob(string attachmentId)
        {
            byte[] data = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT data FROM attachment_blob WHERE attachment_id = @id";
            select.Parameters.AddWithValue("@id", attachmentId);
            object result = select.ExecuteScalar();
            if (result is byte[] bytes)
            {
                data = bytes;
            }
            return data;
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, Attachment attachment)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO attachment (id, note_id, task_id, filename, mime_type, size_bytes, deleted, created_at, updated_at)
                VALUES (@id, @note_id, @task_id, @filename, @mime_type, @size_bytes, @deleted, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    note_id = excluded.note_id, task_id = excluded.task_id, filename = excluded.filename,
                    mime_type = excluded.mime_type, size_bytes = excluded.size_bytes, deleted = excluded.deleted,
                    created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", attachment.Id);
            upsert.Parameters.AddWithValue("@note_id", (object)attachment.NoteId ?? System.DBNull.Value);
            upsert.Parameters.AddWithValue("@task_id", (object)attachment.TaskId ?? System.DBNull.Value);
            upsert.Parameters.AddWithValue("@filename", attachment.Filename ?? string.Empty);
            upsert.Parameters.AddWithValue("@mime_type", attachment.MimeType ?? string.Empty);
            upsert.Parameters.AddWithValue("@size_bytes", attachment.SizeBytes);
            upsert.Parameters.AddWithValue("@deleted", attachment.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@created_at", attachment.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", attachment.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM attachment WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private const string SelectSql =
            "SELECT id, note_id, task_id, filename, mime_type, size_bytes, deleted, created_at, updated_at FROM attachment";

        private static Attachment ReadAttachment(SqliteDataReader reader)
        {
            return new Attachment
            {
                Id = reader.GetString(0),
                NoteId = reader.IsDBNull(1) ? null : reader.GetString(1),
                TaskId = reader.IsDBNull(2) ? null : reader.GetString(2),
                Filename = reader.GetString(3),
                MimeType = reader.GetString(4),
                SizeBytes = reader.GetInt64(5),
                Deleted = reader.GetInt64(6) != 0,
                CreatedAt = reader.GetString(7),
                UpdatedAt = reader.GetString(8),
            };
        }
    }
}
