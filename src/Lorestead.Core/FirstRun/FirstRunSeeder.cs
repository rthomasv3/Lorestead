using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;

namespace Lorestead.Core.FirstRun
{
    // Runs once, for whichever host created the database - the client or the stdio
    // MCP binary (decisions.md). Writes go through the same *Within statics the item
    // repositories use, so the change log and the derived link index come out exactly
    // as they would for hand-typed content; what the repositories' Save would do
    // differently is stamp UtcNow, and the seed needs its baked past timestamp.
    public static class FirstRunSeeder
    {
        // Version 0 of every seeded item by definition, so a real edit or deletion on
        // any device is newer and wins LWW - a fresh device cannot resurrect deleted
        // seed content or overwrite edits made elsewhere (decisions.md).
        private const string SeedTimestamp = "2026-01-01T00:00:00.0000000Z";

        private const string ResourcePrefix = "Lorestead.Core.FirstRun.Content.";
        private const string IconResource = "icon-256.png";
        private const string IconFilename = "icon-256.png";
        private const string IconMimeType = "image/png";

        // The keys FractionalIndex.Between hands out for a list built one append at a
        // time, so seeded siblings sit in the same keyspace shape as user-made ones.
        private static readonly string[] Positions = { "V", "W", "X", "Y", "Z", "a" };

        public static void Seed(ConnectionManager connectionManager, string deviceId)
        {
            using SqliteConnection connection = connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            WriteNotes(connection, transaction, BuildNotes(), deviceId);
            WriteIconAttachment(connection, transaction, deviceId);
            WriteBoard(connection, transaction, deviceId);

            transaction.Commit();
        }

        // Rows first, links second: the tour notes point at each other, and
        // NoteLinkRebuilder drops targets that do not exist yet.
        private static void WriteNotes(SqliteConnection connection, SqliteTransaction transaction, List<Note> notes, string deviceId)
        {
            foreach (Note note in notes)
            {
                NoteRepository.UpsertWithin(connection, transaction, note);
            }

            foreach (Note note in notes)
            {
                NoteLinkRebuilder.RebuildForNoteWithin(connection, transaction, note.Id, note.Body);
                Append(connection, transaction, ItemTypes.Note, note.Id, PayloadJson.Serialize(note), deviceId);
            }
        }

        private static List<Note> BuildNotes()
        {
            List<Note> notes = new List<Note>
            {
                MakeNote(SeedIds.GettingStartedNote, null, "Getting Started", "getting-started.md", Positions[0], NoteType.Normal),
                MakeNote(SeedIds.EditorNote, SeedIds.GettingStartedNote, "Editor & Markdown", "editor-and-markdown.md", Positions[0], NoteType.Normal),
                MakeNote(SeedIds.BoardsNote, SeedIds.GettingStartedNote, "Boards & Tasks", "boards-and-tasks.md", Positions[1], NoteType.Normal),
                MakeNote(SeedIds.TemplatesNote, SeedIds.GettingStartedNote, "Templates", "templates.md", Positions[2], NoteType.Normal),
                MakeNote(SeedIds.SearchNote, SeedIds.GettingStartedNote, "Search & Links", "search-and-links.md", Positions[3], NoteType.Normal),
                MakeNote(SeedIds.SyncNote, SeedIds.GettingStartedNote, "Sync Setup", "sync-setup.md", Positions[4], NoteType.Normal),
                MakeNote(SeedIds.AgentsNote, SeedIds.GettingStartedNote, "Agents & MCP", "agents-and-mcp.md", Positions[5], NoteType.Normal),

                MakeNote(SeedIds.ProjectTemplate, null, "Project", "template-project.md", Positions[1], NoteType.Template),
                MakeNote(SeedIds.ProjectOverview, SeedIds.ProjectTemplate, "Overview", "template-overview.md", Positions[0], NoteType.Normal),
                MakeNote(SeedIds.ProjectIdeas, SeedIds.ProjectTemplate, "Ideas", "template-ideas.md", Positions[1], NoteType.Normal),
                MakeNote(SeedIds.ProjectLog, SeedIds.ProjectTemplate, "Log", "template-log.md", Positions[2], NoteType.Normal),
            };
            return notes;
        }

        private static Note MakeNote(string id, string parentId, string title, string resource, string position, NoteType type)
        {
            return new Note
            {
                Id = id,
                ParentId = parentId,
                Title = title,
                Body = ReadText(resource),
                Position = position,
                Type = type,
                Deleted = false,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp,
            };
        }

        // The blob goes in before the row's transaction commits, so a crash leaves an
        // empty database rather than an attachment pointing at nothing (decisions.md).
        private static void WriteIconAttachment(SqliteConnection connection, SqliteTransaction transaction, string deviceId)
        {
            byte[] data = ReadBytes(IconResource);
            Attachment attachment = new Attachment
            {
                Id = SeedIds.IconAttachment,
                NoteId = SeedIds.GettingStartedNote,
                Filename = IconFilename,
                MimeType = IconMimeType,
                SizeBytes = data.Length,
                Deleted = false,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp,
            };

            AttachmentRepository.UpsertWithin(connection, transaction, attachment);
            InsertBlob(connection, attachment.Id, data);
            Append(connection, transaction, ItemTypes.Attachment, attachment.Id, PayloadJson.Serialize(attachment), deviceId);
        }

        // No thumbnail is written: Core has no imaging dependency, and the frontend's
        // lazy rebuild - the path that covers attachments arriving from sync - renders
        // one the first time the note is opened (decisions.md).
        private static void InsertBlob(SqliteConnection connection, string attachmentId, byte[] data)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO attachment_blob (attachment_id, data) VALUES (@id, @data)";
            insert.Parameters.AddWithValue("@id", attachmentId);
            insert.Parameters.AddWithValue("@data", data);
            insert.ExecuteNonQuery();
        }

        private static void WriteBoard(SqliteConnection connection, SqliteTransaction transaction, string deviceId)
        {
            Board board = new Board
            {
                Id = SeedIds.LearnBoard,
                Name = "Learn Lorestead",
                Position = Positions[0],
                Deleted = false,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp,
            };
            BoardRepository.UpsertWithin(connection, transaction, board);
            Append(connection, transaction, ItemTypes.Board, board.Id, PayloadJson.Serialize(board), deviceId);

            WriteColumn(connection, transaction, SeedIds.ToTryColumn, "To Try", Positions[0], deviceId);
            WriteColumn(connection, transaction, SeedIds.DoneColumn, "Done", Positions[1], deviceId);

            WriteTask(connection, transaction, SeedIds.FirstNoteTask, "Write your first note", "task-first-note.md", Positions[0], SeedIds.EditorNote, deviceId);
            WriteTask(connection, transaction, SeedIds.LinkNotesTask, "Link two notes together", "task-link-notes.md", Positions[1], SeedIds.SearchNote, deviceId);
            WriteTask(connection, transaction, SeedIds.AttachImageTask, "Attach an image", "task-attach-image.md", Positions[2], SeedIds.EditorNote, deviceId);
            WriteTask(connection, transaction, SeedIds.MakeTemplateTask, "Make a template", "task-make-template.md", Positions[3], SeedIds.TemplatesNote, deviceId);
            WriteTask(connection, transaction, SeedIds.SetUpSyncTask, "Set up sync", "task-set-up-sync.md", Positions[4], SeedIds.SyncNote, deviceId);
            WriteTask(connection, transaction, SeedIds.ConnectAgentTask, "Connect an agent", "task-connect-agent.md", Positions[5], SeedIds.AgentsNote, deviceId);
        }

        private static void WriteColumn(SqliteConnection connection, SqliteTransaction transaction, string id, string name, string position, string deviceId)
        {
            BoardColumn column = new BoardColumn
            {
                Id = id,
                BoardId = SeedIds.LearnBoard,
                Name = name,
                Position = position,
                Deleted = false,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp,
            };
            BoardColumnRepository.UpsertWithin(connection, transaction, column);
            Append(connection, transaction, ItemTypes.Column, column.Id, PayloadJson.Serialize(column), deviceId);
        }

        // Each card both links its note in the body and carries it in the linked-notes
        // list, so the note's backlinks panel shows the two sources as one card and
        // both halves of the feature are visible from the start (features/links.md).
        private static void WriteTask(SqliteConnection connection, SqliteTransaction transaction, string id, string title, string resource, string position, string noteId, string deviceId)
        {
            TaskItem task = new TaskItem
            {
                Id = id,
                ColumnId = SeedIds.ToTryColumn,
                Title = title,
                Body = ReadText(resource),
                Position = position,
                Deleted = false,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp,
                NoteIds = new List<string> { noteId },
            };

            TaskRepository.UpsertWithin(connection, transaction, task);
            TaskRepository.ReplaceNoteLinksWithin(connection, transaction, task.Id, task.NoteIds);
            NoteLinkRebuilder.RebuildForTaskWithin(connection, transaction, task.Id, task.Body);
            Append(connection, transaction, ItemTypes.Task, task.Id, PayloadJson.Serialize(task), deviceId);
        }

        // base_seq stays null: these are creates, and on a database this host just
        // built there is no earlier version of any of them to have been based on.
        private static void Append(SqliteConnection connection, SqliteTransaction transaction, string itemType, string itemId, string payload, string deviceId)
        {
            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = itemType,
                ItemId = itemId,
                Op = ChangeOps.Upsert,
                Payload = payload,
                BaseSeq = null,
                DeviceId = deviceId,
                ChangedAt = SeedTimestamp,
            });
        }

        // Line endings are normalized so the stored body does not depend on how the
        // repository was checked out.
        private static string ReadText(string resource)
        {
            string text;
            using (Stream stream = OpenResource(resource))
            {
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                text = reader.ReadToEnd();
            }
            return text.Replace("\r\n", "\n");
        }

        private static byte[] ReadBytes(string resource)
        {
            using Stream stream = OpenResource(resource);
            using MemoryStream buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static Stream OpenResource(string resource)
        {
            Stream stream = typeof(FirstRunSeeder).Assembly.GetManifestResourceStream(ResourcePrefix + resource);
            if (stream == null)
            {
                throw new FileNotFoundException("Seed resource is missing from the assembly.", resource);
            }
            return stream;
        }
    }
}
