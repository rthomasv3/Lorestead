using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;

namespace SylvaNote.Core.DataAccess
{
    public sealed class AttachmentRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public AttachmentRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
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

            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Attachment,
                ItemId = attachment.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(attachment),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Attachment, attachment.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);

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

        public List<Attachment> GetForNote(string noteId)
        {
            List<Attachment> attachments = new List<Attachment>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE note_id = @note_id AND deleted = 0 ORDER BY created_at";
            select.Parameters.AddWithValue("@note_id", noteId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                attachments.Add(ReadAttachment(reader));
            }
            return attachments;
        }

        // One query for the whole export rather than one per note; the caller keeps
        // only the rows whose owner is in scope.
        public List<Attachment> GetAllForNotes()
        {
            List<Attachment> attachments = new List<Attachment>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE note_id IS NOT NULL AND deleted = 0 ORDER BY created_at";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                attachments.Add(ReadAttachment(reader));
            }
            return attachments;
        }

        public List<Attachment> GetForTask(string taskId)
        {
            List<Attachment> attachments = new List<Attachment>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE task_id = @task_id AND deleted = 0 ORDER BY created_at";
            select.Parameters.AddWithValue("@task_id", taskId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                attachments.Add(ReadAttachment(reader));
            }
            return attachments;
        }

        // Per-task counts for kanban card badges, one query per board load.
        public Dictionary<string, int> CountByTaskForBoard(string boardId)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT a.task_id, COUNT(*)
                FROM attachment a
                JOIN task t ON t.id = a.task_id
                JOIN board_column bc ON bc.id = t.column_id
                WHERE bc.board_id = @board_id AND a.deleted = 0
                GROUP BY a.task_id";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }
            return counts;
        }

        // Thumbnails are a device-local derived cache - regenerated by the frontend
        // whenever it has the full image, so REPLACE keeps the latest render.
        public void SaveThumbnail(string attachmentId, byte[] data)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO attachment_thumbnail (attachment_id, data) VALUES (@id, @data)";
            insert.Parameters.AddWithValue("@id", attachmentId);
            insert.Parameters.AddWithValue("@data", data);
            insert.ExecuteNonQuery();
        }

        public byte[] GetThumbnail(string attachmentId)
        {
            byte[] data = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT data FROM attachment_thumbnail WHERE attachment_id = @id";
            select.Parameters.AddWithValue("@id", attachmentId);
            object result = select.ExecuteScalar();
            if (result is byte[] bytes)
            {
                data = bytes;
            }
            return data;
        }

        // Blobs never touch the change log (data.md) - they move over dedicated endpoints.
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

        // Sync backfill: live attachments whose blob has not arrived yet.
        public List<string> GetIdsMissingBlob()
        {
            List<string> ids = new List<string>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT a.id FROM attachment a
                LEFT JOIN attachment_blob b ON b.attachment_id = a.id
                WHERE b.attachment_id IS NULL AND a.deleted = 0";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
            return ids;
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
