using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;

namespace SylvaNote.Core.DataAccess
{
    public sealed class NoteRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;

        public NoteRepository(ConnectionManager connectionManager, string deviceId)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
        }

        public void Save(Note note)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(note.CreatedAt))
            {
                note.CreatedAt = now;
            }
            note.UpdatedAt = now;

            UpsertWithin(connection, transaction, note);
            NoteLinkRebuilder.RebuildForNoteWithin(connection, transaction, note.Id, note.Body);

            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Note,
                ItemId = note.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(note),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Note, note.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            });

            transaction.Commit();
        }

        public Note Get(string id)
        {
            Note note = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                note = ReadNote(reader);
            }
            return note;
        }

        public List<Note> GetAll()
        {
            List<Note> notes = new List<Note>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " ORDER BY position";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                notes.Add(ReadNote(reader));
            }
            return notes;
        }

        public List<NoteLink> GetBacklinks(string noteId)
        {
            List<NoteLink> links = new List<NoteLink>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT from_note_id, from_task_id, to_note_id FROM note_link WHERE to_note_id = @id";
            select.Parameters.AddWithValue("@id", noteId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                links.Add(new NoteLink
                {
                    FromNoteId = reader.IsDBNull(0) ? null : reader.GetString(0),
                    FromTaskId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ToNoteId = reader.GetString(2),
                });
            }
            return links;
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, Note note)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO note (id, parent_id, title, body, position, type, deleted, created_at, updated_at)
                VALUES (@id, @parent_id, @title, @body, @position, @type, @deleted, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    parent_id = excluded.parent_id, title = excluded.title, body = excluded.body,
                    position = excluded.position, type = excluded.type, deleted = excluded.deleted,
                    created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", note.Id);
            upsert.Parameters.AddWithValue("@parent_id", (object)note.ParentId ?? System.DBNull.Value);
            upsert.Parameters.AddWithValue("@title", note.Title ?? string.Empty);
            upsert.Parameters.AddWithValue("@body", note.Body ?? string.Empty);
            upsert.Parameters.AddWithValue("@position", note.Position);
            upsert.Parameters.AddWithValue("@type", (int)note.Type);
            upsert.Parameters.AddWithValue("@deleted", note.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@created_at", note.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", note.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM note WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private const string SelectSql =
            "SELECT id, parent_id, title, body, position, type, deleted, created_at, updated_at FROM note";

        private static Note ReadNote(SqliteDataReader reader)
        {
            return new Note
            {
                Id = reader.GetString(0),
                ParentId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Title = reader.GetString(2),
                Body = reader.GetString(3),
                Position = reader.GetString(4),
                Type = (NoteType)reader.GetInt32(5),
                Deleted = reader.GetInt64(6) != 0,
                CreatedAt = reader.GetString(7),
                UpdatedAt = reader.GetString(8),
            };
        }
    }
}
