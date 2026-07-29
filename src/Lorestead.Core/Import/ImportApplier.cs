using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Lorestead.Core.Ordering;
using Lorestead.Core.Sync;

namespace Lorestead.Core.Import
{
    // Applies an ImportPlan in one transaction: a hard failure imports nothing
    // (features/import.md). Writes go through the same *Within statics the item
    // repositories use, so the change log, the derived link index, and sync come
    // out exactly as they would for hand-typed content (the FirstRunSeeder
    // precedent) - "file wins" rides the normal save path.
    public static class ImportApplier
    {
        public static void Apply(
            ConnectionManager connectionManager,
            string deviceId,
            int historyRetention,
            ImportPlan plan,
            Func<string, byte[]> readFile)
        {
            using SqliteConnection connection = connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            Dictionary<string, string> lastPositionByParent = new Dictionary<string, string>();
            List<Note> written = new List<Note>();

            foreach (ImportedNote planned in plan.Notes)
            {
                if (planned.Action != ImportAction.SkipIdentical)
                {
                    Note note = planned.Action == ImportAction.Merge
                        ? ReadNoteWithin(connection, planned.Id)
                        : null;

                    if (note != null)
                    {
                        note.Title = planned.Title;
                        note.Body = planned.Body;
                        note.UpdatedAt = now;
                    }
                    else
                    {
                        // A merge target deleted between preflight and apply falls
                        // back to a create, keeping its carried id.
                        note = new Note
                        {
                            Id = planned.Id,
                            ParentId = planned.ParentId,
                            Title = planned.Title,
                            Body = planned.Body,
                            Position = NextPosition(connection, planned.ParentId, lastPositionByParent),
                            Type = planned.Type,
                            Deleted = false,
                            CreatedAt = planned.CreatedAt ?? now,
                            UpdatedAt = planned.UpdatedAt ?? now,
                        };
                    }

                    NoteRepository.UpsertWithin(connection, transaction, note);
                    written.Add(note);
                }
            }

            // Rows first, links and history second: imported notes link to each
            // other, and NoteLinkRebuilder drops targets that do not exist yet.
            foreach (Note note in written)
            {
                NoteLinkRebuilder.RebuildForNoteWithin(connection, transaction, note.Id, note.Body);
                AppendChangeWithin(connection, transaction, ItemTypes.Note, note.Id,
                    PayloadJson.Serialize(note), deviceId, now, historyRetention);
            }

            foreach (ImportedAttachment planned in plan.Attachments)
            {
                byte[] data = readFile(planned.SourcePath) ?? new byte[0];
                Attachment attachment = new Attachment
                {
                    Id = planned.Id,
                    NoteId = planned.NoteId,
                    Filename = planned.Filename,
                    MimeType = planned.MimeType,
                    SizeBytes = data.LongLength,
                    Deleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                AttachmentRepository.UpsertWithin(connection, transaction, attachment);
                InsertBlobWithin(connection, attachment.Id, data);
                AppendChangeWithin(connection, transaction, ItemTypes.Attachment, attachment.Id,
                    PayloadJson.Serialize(attachment), deviceId, now, historyRetention);
            }

            transaction.Commit();
        }

        // Created siblings chain from the parent's current last position, so an
        // import appends in file order without renumbering anything.
        private static string NextPosition(
            SqliteConnection connection,
            string parentId,
            Dictionary<string, string> lastPositionByParent)
        {
            string key = parentId ?? string.Empty;
            string last;

            if (!lastPositionByParent.TryGetValue(key, out last))
            {
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = parentId == null
                    ? "SELECT MAX(position) FROM note WHERE parent_id IS NULL"
                    : "SELECT MAX(position) FROM note WHERE parent_id = @parent_id";
                if (parentId != null)
                {
                    select.Parameters.AddWithValue("@parent_id", parentId);
                }
                object result = select.ExecuteScalar();
                last = result is string value ? value : null;
            }

            string next = FractionalIndex.Between(last, null);
            lastPositionByParent[key] = next;
            return next;
        }

        private static Note ReadNoteWithin(SqliteConnection connection, string id)
        {
            Note note = null;
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText =
                "SELECT id, parent_id, title, body, position, type, deleted, created_at, updated_at FROM note WHERE id = @id AND deleted = 0";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                note = new Note
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
            return note;
        }

        // The blob goes in before the transaction commits, so a failure leaves no
        // attachment row pointing at nothing.
        private static void InsertBlobWithin(SqliteConnection connection, string attachmentId, byte[] data)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO attachment_blob (attachment_id, data) VALUES (@id, @data)";
            insert.Parameters.AddWithValue("@id", attachmentId);
            insert.Parameters.AddWithValue("@data", data);
            insert.ExecuteNonQuery();
        }

        private static void AppendChangeWithin(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string itemType,
            string itemId,
            string payload,
            string deviceId,
            string now,
            int historyRetention)
        {
            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = itemType,
                ItemId = itemId,
                Op = ChangeOps.Upsert,
                Payload = payload,
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, itemType, itemId),
                DeviceId = deviceId,
                ChangedAt = now,
            }, historyRetention);
        }
    }
}
