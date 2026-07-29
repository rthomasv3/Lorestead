using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.Entities;
using Lorestead.Core.Notes;
using Lorestead.Core.Ordering;
using Lorestead.Core.Sync;

namespace Lorestead.Core.DataAccess
{
    public sealed class NoteRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public NoteRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
        }

        public void Save(Note note)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            // Every local write lands here - client services, MCP tools, template
            // instantiation - so this is the one place the hygiene rule has to hold.
            // Changes arriving from sync go through UpsertWithin instead and are left
            // exactly as the originating device wrote them.
            note.Title = NoteTitle.Normalize(note.Title);

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(note.CreatedAt))
            {
                note.CreatedAt = now;
            }
            note.UpdatedAt = now;

            UpsertWithin(connection, transaction, note);
            NoteLinkRebuilder.RebuildForNoteWithin(connection, transaction, note.Id, note.Body);

            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Note,
                ItemId = note.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(note),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Note, note.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);

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

        // Everything that points at this note, from both directions: bodies that
        // mention it (note_link, derived by parsing) and tasks that carry it in their
        // linked-notes list (task_note, authored). A task doing both is one card, not
        // two. Trashed notes and deleted tasks/columns/boards are excluded - a source
        // in the trash is not a live reference.
        public List<NoteBacklink> GetBacklinkSources(string noteId)
        {
            List<NoteBacklink> backlinks = new List<NoteBacklink>();
            List<NoteBacklink> taskSources = new List<NoteBacklink>();
            Dictionary<string, NoteBacklink> taskSourcesById = new Dictionary<string, NoteBacklink>();
            using SqliteConnection connection = _connectionManager.CreateConnection();

            using (SqliteCommand fromNotes = connection.CreateCommand())
            {
                fromNotes.CommandText = @"
                    SELECT n.id, n.title, n.body
                    FROM note_link nl
                    JOIN note n ON n.id = nl.from_note_id
                    WHERE nl.to_note_id = @id AND n.deleted = 0
                    ORDER BY n.title";
                fromNotes.Parameters.AddWithValue("@id", noteId);
                using SqliteDataReader reader = fromNotes.ExecuteReader();
                while (reader.Read())
                {
                    backlinks.Add(new NoteBacklink
                    {
                        NoteId = reader.GetString(0),
                        Title = reader.GetString(1),
                        Snippet = NoteLinkRebuilder.ContextSnippet(reader.GetString(2), noteId),
                        Via = BacklinkVia.Body,
                    });
                }
            }

            using (SqliteCommand fromTaskBodies = connection.CreateCommand())
            {
                fromTaskBodies.CommandText = @"
                    SELECT t.id, t.title, t.body, bc.name, b.id, b.name
                    FROM note_link nl
                    JOIN task t ON t.id = nl.from_task_id
                    JOIN board_column bc ON bc.id = t.column_id
                    JOIN board b ON b.id = bc.board_id
                    WHERE nl.to_note_id = @id AND t.deleted = 0 AND bc.deleted = 0 AND b.deleted = 0";
                fromTaskBodies.Parameters.AddWithValue("@id", noteId);
                using SqliteDataReader reader = fromTaskBodies.ExecuteReader();
                while (reader.Read())
                {
                    NoteBacklink source = ReadTaskSource(reader);
                    source.Snippet = NoteLinkRebuilder.ContextSnippet(reader.GetString(2), noteId);
                    source.Via = BacklinkVia.Body;
                    taskSources.Add(source);
                    taskSourcesById[source.TaskId] = source;
                }
            }

            using (SqliteCommand linkedTasks = connection.CreateCommand())
            {
                linkedTasks.CommandText = @"
                    SELECT t.id, t.title, t.body, bc.name, b.id, b.name
                    FROM task_note tn
                    JOIN task t ON t.id = tn.task_id
                    JOIN board_column bc ON bc.id = t.column_id
                    JOIN board b ON b.id = bc.board_id
                    WHERE tn.note_id = @id AND t.deleted = 0 AND bc.deleted = 0 AND b.deleted = 0";
                linkedTasks.Parameters.AddWithValue("@id", noteId);
                using SqliteDataReader reader = linkedTasks.ExecuteReader();
                while (reader.Read())
                {
                    string taskId = reader.GetString(0);
                    if (taskSourcesById.TryGetValue(taskId, out NoteBacklink mentioned))
                    {
                        mentioned.Via = BacklinkVia.Both;
                    }
                    else
                    {
                        NoteBacklink source = ReadTaskSource(reader);
                        source.Via = BacklinkVia.Link;
                        taskSources.Add(source);
                        taskSourcesById[taskId] = source;
                    }
                }
            }

            taskSources.Sort((left, right) => string.CompareOrdinal(left.Title, right.Title));
            backlinks.AddRange(taskSources);
            return backlinks;
        }

        private static NoteBacklink ReadTaskSource(SqliteDataReader reader)
        {
            return new NoteBacklink
            {
                TaskId = reader.GetString(0),
                Title = reader.GetString(1),
                Snippet = string.Empty,
                ColumnName = reader.GetString(3),
                BoardId = reader.GetString(4),
                BoardName = reader.GetString(5),
            };
        }

        // Position uniqueness is checked against ALL children of a parent - trashed and
        // template siblings share the fractional keyspace even though they render in
        // separate groups, and a restored sibling must not collide.
        public string GetMaxChildPosition(string parentId)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = parentId == null
                ? "SELECT MAX(position) FROM note WHERE parent_id IS NULL"
                : "SELECT MAX(position) FROM note WHERE parent_id = @parent_id";
            if (parentId != null)
            {
                select.Parameters.AddWithValue("@parent_id", parentId);
            }
            object result = select.ExecuteScalar();
            return result is string value ? value : null;
        }

        // Nearest sibling key above `afterPosition`, again across ALL children: with
        // both bounds from the shared keyspace, a key between them cannot collide
        // with a hidden sibling.
        public string GetNextChildPosition(string parentId, string afterPosition)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = parentId == null
                ? "SELECT MIN(position) FROM note WHERE parent_id IS NULL AND position > @after"
                : "SELECT MIN(position) FROM note WHERE parent_id = @parent_id AND position > @after";
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
                ? "SELECT 1 FROM note WHERE parent_id IS NULL AND position = @position LIMIT 1"
                : "SELECT 1 FROM note WHERE parent_id = @parent_id AND position = @position LIMIT 1";
            if (parentId != null)
            {
                select.Parameters.AddWithValue("@parent_id", parentId);
            }
            select.Parameters.AddWithValue("@position", position);
            return select.ExecuteScalar() != null;
        }

        // Trash acts on whole subtrees, mirroring delete semantics in ui/pages/notes.md:
        // every descendant is tombstoned with the root, each as its own outbox entry.
        // parent_id is never disturbed, so restore keeps the original location.
        public void TrashSubtree(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            foreach (Note note in ReadSubtreeWithin(connection, transaction, id))
            {
                if (!note.Deleted)
                {
                    note.Deleted = true;
                    note.UpdatedAt = now;
                    UpsertWithin(connection, transaction, note);
                    AppendNoteChangeWithin(connection, transaction, note, now);
                }
            }

            transaction.Commit();
        }

        // Restore also acts on the whole subtree. A restored child whose parent is still
        // trashed goes to root level (ui/pages/notes.md - plain Restore on a child).
        public void RestoreSubtree(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            RestoreSubtreeWithin(connection, transaction, id, null, null, false);

            transaction.Commit();
        }

        // Drag-out restore: the drop location is explicit, so the root is re-parented and
        // repositioned as part of the same restore.
        public void RestoreSubtreeAt(string id, string parentId, string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            RestoreSubtreeWithin(connection, transaction, id, parentId, position, true);

            transaction.Commit();
        }

        public void RestoreWithAncestors(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string topmost = id;
            string current = id;
            while (current != null)
            {
                Note note = GetWithin(connection, transaction, current);
                if (note == null)
                {
                    current = null;
                }
                else
                {
                    if (note.Deleted)
                    {
                        topmost = note.Id;
                    }
                    current = note.ParentId;
                }
            }

            RestoreSubtreeWithin(connection, transaction, topmost, null, null, false);

            transaction.Commit();
        }

        // Purge is permanent: subtree note rows go away (FK cascades take attachments,
        // blobs, and note_links), per-item history is deleted, and a pending purge entry
        // is appended per note and per attachment so other devices follow suit.
        public void PurgeSubtree(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            PurgeSubtreeWithin(connection, transaction, id);

            transaction.Commit();
        }

        public void PurgeExpiredTrash(string cutoffIso)
        {
            List<string> expired = new List<string>();
            using (SqliteConnection connection = _connectionManager.CreateConnection())
            {
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = "SELECT id FROM note WHERE deleted = 1 AND updated_at < @cutoff";
                select.Parameters.AddWithValue("@cutoff", cutoffIso);
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read())
                {
                    expired.Add(reader.GetString(0));
                }
            }

            foreach (string noteId in expired)
            {
                // A note already removed by an earlier subtree purge makes this a no-op.
                PurgeSubtree(noteId);
            }
        }

        public string InstantiateTemplate(string templateId, string title, string parentId, string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string newRootId = CopySubtreeWithin(connection, transaction, templateId, title, parentId, position, forceNormalType: true);

            transaction.Commit();
            return newRootId;
        }

        // Duplicate lands as a sibling of the original (same parent; the caller picks
        // the position, typically just after it). Types are preserved so a duplicated
        // template stays a template; trashed descendants stay behind.
        public string DuplicateSubtree(string noteId, string title, string position)
        {
            string newRootId = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Note original = GetWithin(connection, transaction, noteId);
            if (original != null)
            {
                newRootId = CopySubtreeWithin(connection, transaction, noteId, title, original.ParentId, position, forceNormalType: false);
            }

            transaction.Commit();
            return newRootId;
        }

        private string CopySubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string rootId, string rootTitle, string rootParentId, string rootPosition, bool forceNormalType)
        {
            string newRootId = null;
            List<Note> subtree = ReadSubtreeWithin(connection, transaction, rootId, activeOnly: true);
            if (subtree.Count > 0)
            {
                string now = Timestamps.UtcNowIso();
                Dictionary<string, string> idMap = new Dictionary<string, string>();
                foreach (Note note in subtree)
                {
                    idMap[note.Id] = Guid.CreateVersion7().ToString();
                }

                foreach (Note note in subtree)
                {
                    bool isRoot = note.Id == rootId;
                    Note copy = new Note
                    {
                        Id = idMap[note.Id],
                        ParentId = isRoot ? rootParentId : idMap[note.ParentId],
                        Title = isRoot ? rootTitle : note.Title,
                        Body = note.Body,
                        Position = isRoot ? rootPosition : note.Position,
                        Type = forceNormalType ? NoteType.Normal : note.Type,
                        Deleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    UpsertWithin(connection, transaction, copy);
                    NoteLinkRebuilder.RebuildForNoteWithin(connection, transaction, copy.Id, copy.Body);
                    AppendNoteChangeWithin(connection, transaction, copy, now);
                    CopyAttachmentsWithin(connection, transaction, note.Id, copy.Id, now);
                }

                newRootId = idMap[rootId];
            }
            return newRootId;
        }

        // A template's attachments come with the copy: the user attached them on
        // purpose, and a body that embeds one would otherwise point at nothing. The
        // thumbnail is copied too - the image is byte-identical, so regenerating it
        // would decode the blob only to store the same bytes again.
        private void CopyAttachmentsWithin(SqliteConnection connection, SqliteTransaction transaction, string sourceNoteId, string targetNoteId, string now)
        {
            // Read fully before writing - a reader held open over the same connection
            // while inserting is asking for trouble.
            List<Attachment> sources = new List<Attachment>();
            using (SqliteCommand select = connection.CreateCommand())
            {
                select.CommandText = "SELECT id, filename, mime_type, size_bytes FROM attachment WHERE note_id = @note_id AND deleted = 0";
                select.Parameters.AddWithValue("@note_id", sourceNoteId);
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read())
                {
                    sources.Add(new Attachment
                    {
                        Id = reader.GetString(0),
                        Filename = reader.GetString(1),
                        MimeType = reader.GetString(2),
                        SizeBytes = reader.GetInt64(3),
                    });
                }
            }

            foreach (Attachment source in sources)
            {
                Attachment copy = new Attachment
                {
                    Id = Guid.CreateVersion7().ToString(),
                    NoteId = targetNoteId,
                    Filename = source.Filename,
                    MimeType = source.MimeType,
                    SizeBytes = source.SizeBytes,
                    Deleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                AttachmentRepository.UpsertWithin(connection, transaction, copy);
                CopyBlobRowWithin(connection, "attachment_blob", source.Id, copy.Id);
                CopyBlobRowWithin(connection, "attachment_thumbnail", source.Id, copy.Id);

                ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.Attachment,
                    ItemId = copy.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(copy),
                    BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Attachment, copy.Id),
                    DeviceId = _deviceId,
                    ChangedAt = now,
                }, _historyRetention);
            }
        }

        // INSERT..SELECT so the bytes never leave SQLite. A missing source row (no
        // thumbnail yet) simply copies nothing.
        private static void CopyBlobRowWithin(SqliteConnection connection, string table, string sourceId, string targetId)
        {
            using SqliteCommand copy = connection.CreateCommand();
            copy.CommandText = $"INSERT INTO {table} (attachment_id, data) SELECT @target, data FROM {table} WHERE attachment_id = @source";
            copy.Parameters.AddWithValue("@target", targetId);
            copy.Parameters.AddWithValue("@source", sourceId);
            copy.ExecuteNonQuery();
        }

        private void RestoreSubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string id, string parentId, string position, bool placeExplicitly)
        {
            string now = Timestamps.UtcNowIso();
            foreach (Note note in ReadSubtreeWithin(connection, transaction, id))
            {
                if (note.Deleted)
                {
                    note.Deleted = false;
                    note.UpdatedAt = now;

                    if (note.Id == id)
                    {
                        if (placeExplicitly)
                        {
                            note.ParentId = parentId;
                            note.Position = position;
                        }
                        else if (note.ParentId != null)
                        {
                            Note parent = GetWithin(connection, transaction, note.ParentId);
                            if (parent == null || parent.Deleted)
                            {
                                note.ParentId = null;
                                note.Position = NextRootPositionWithin(connection, transaction);
                            }
                        }
                    }

                    UpsertWithin(connection, transaction, note);
                    AppendNoteChangeWithin(connection, transaction, note, now);
                }
            }
        }

        private void PurgeSubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            string now = Timestamps.UtcNowIso();
            List<Note> subtree = ReadSubtreeWithin(connection, transaction, id);

            foreach (Note note in subtree)
            {
                foreach (string attachmentId in ReadAttachmentIdsWithin(connection, transaction, note.Id))
                {
                    ChangeLogRepository.DeleteForItemWithin(connection, transaction, ItemTypes.Attachment, attachmentId);
                    ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
                    {
                        ItemType = ItemTypes.Attachment,
                        ItemId = attachmentId,
                        Op = ChangeOps.Purge,
                        DeviceId = _deviceId,
                        ChangedAt = now,
                    });
                }
            }

            foreach (Note note in subtree)
            {
                DeleteRowWithin(connection, transaction, note.Id);
                ChangeLogRepository.DeleteForItemWithin(connection, transaction, ItemTypes.Note, note.Id);
                ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.Note,
                    ItemId = note.Id,
                    Op = ChangeOps.Purge,
                    DeviceId = _deviceId,
                    ChangedAt = now,
                });
            }
        }

        private void AppendNoteChangeWithin(SqliteConnection connection, SqliteTransaction transaction, Note note, string now)
        {
            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Note,
                ItemId = note.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(note),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Note, note.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);
        }

        private static Note GetWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            Note note = null;
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

        private static List<Note> ReadSubtreeWithin(SqliteConnection connection, SqliteTransaction transaction, string rootId, bool activeOnly = false)
        {
            List<Note> notes = new List<Note>();
            using SqliteCommand select = connection.CreateCommand();
            string recursionFilter = activeOnly ? " WHERE n.deleted = 0" : string.Empty;
            select.CommandText = $@"
                WITH RECURSIVE sub (id) AS (
                    SELECT id FROM note WHERE id = @id
                    UNION ALL
                    SELECT n.id FROM note n JOIN sub ON n.parent_id = sub.id{recursionFilter}
                )
                {SelectSql} WHERE id IN (SELECT id FROM sub)";
            select.Parameters.AddWithValue("@id", rootId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                notes.Add(ReadNote(reader));
            }
            return notes;
        }

        private static List<string> ReadAttachmentIdsWithin(SqliteConnection connection, SqliteTransaction transaction, string noteId)
        {
            List<string> ids = new List<string>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT id FROM attachment WHERE note_id = @note_id";
            select.Parameters.AddWithValue("@note_id", noteId);
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
            select.CommandText = "SELECT MAX(position) FROM note WHERE parent_id IS NULL AND deleted = 0";
            object result = select.ExecuteScalar();
            string last = result is string value ? value : null;
            return FractionalIndex.Between(last, null);
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
