using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Mcp.Contracts;
using SylvaNote.Core.Ordering;
using SylvaNote.Core.Search;

namespace SylvaNote.Core.Mcp
{
    // The 18 MCP tool operations (features/mcp.md), shared verbatim by the server's
    // HTTP endpoint and the stdio binary. Writes ride the normal repository save path
    // (outbox append); afterWrite is the host seam - the server stamps seqs and
    // broadcasts the sync hint there, the stdio host passes null.
    public sealed class McpToolService
    {
        private const long MaxAttachmentBytes = 100L * 1024 * 1024;

        private readonly Func<Task> _afterWrite;
        private readonly NoteRepository _notes;
        private readonly BoardRepository _boards;
        private readonly BoardColumnRepository _columns;
        private readonly TaskRepository _tasks;
        private readonly AttachmentRepository _attachments;
        private readonly SearchRepository _search;

        public McpToolService(ConnectionManager connectionManager, string deviceId, Func<Task> afterWrite = null)
        {
            _afterWrite = afterWrite;
            _notes = new NoteRepository(connectionManager, deviceId);
            _boards = new BoardRepository(connectionManager, deviceId);
            _columns = new BoardColumnRepository(connectionManager, deviceId);
            _tasks = new TaskRepository(connectionManager, deviceId);
            _attachments = new AttachmentRepository(connectionManager, deviceId);
            _search = new SearchRepository(connectionManager);
        }

        public McpSearchResponse Search(string query)
        {
            McpSearchResponse response = new McpSearchResponse();
            Dictionary<string, Note> notesById = GetNotesById();

            foreach (SearchResult hit in _search.SearchNotes(query))
            {
                response.Notes.Add(new McpNoteHit
                {
                    Id = hit.Id,
                    Title = hit.Title,
                    Breadcrumb = BuildBreadcrumb(notesById, hit.Id),
                    Snippet = hit.Snippet,
                });
            }

            foreach (TaskSearchResult hit in _search.SearchTasksWithContext(query))
            {
                response.Tasks.Add(new McpTaskHit
                {
                    Id = hit.Id,
                    Title = hit.Title,
                    Breadcrumb = $"{hit.BoardName} › {hit.ColumnName}",
                    Snippet = hit.Snippet,
                });
            }

            foreach (SearchResult hit in _search.SearchBoards(query))
            {
                response.Boards.Add(new McpBoardHit { Id = hit.Id, Name = hit.Title });
            }

            return response;
        }

        public McpNoteTreeResponse ListNoteTree()
        {
            McpNoteTreeResponse response = new McpNoteTreeResponse();
            Dictionary<string, McpTreeNode> nodes = new Dictionary<string, McpTreeNode>();
            List<Note> active = new List<Note>();

            foreach (Note note in _notes.GetAll())
            {
                if (!note.Deleted && note.Type == NoteType.Normal)
                {
                    active.Add(note);
                    nodes[note.Id] = new McpTreeNode { Id = note.Id, Title = note.Title };
                }
            }

            foreach (Note note in active)
            {
                if (note.ParentId != null && nodes.TryGetValue(note.ParentId, out McpTreeNode parent))
                {
                    parent.Children.Add(nodes[note.Id]);
                }
                else
                {
                    response.Notes.Add(nodes[note.Id]);
                }
            }

            return response;
        }

        public McpNoteResponse GetNote(string noteId)
        {
            Note note = RequireNote(noteId);
            McpNoteResponse response = new McpNoteResponse
            {
                Id = note.Id,
                ParentId = note.ParentId,
                Title = note.Title,
                Body = note.Body,
                Deleted = note.Deleted,
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt,
            };

            foreach (Attachment attachment in _attachments.GetForNote(noteId))
            {
                response.Attachments.Add(ToAttachmentInfo(attachment));
            }

            foreach (NoteLink link in _notes.GetBacklinks(noteId))
            {
                if (link.FromNoteId != null)
                {
                    Note source = _notes.Get(link.FromNoteId);
                    if (source != null)
                    {
                        response.Backlinks.Add(new McpBacklink { NoteId = source.Id, Title = source.Title });
                    }
                }
                else if (link.FromTaskId != null)
                {
                    TaskItem source = _tasks.Get(link.FromTaskId);
                    if (source != null)
                    {
                        response.Backlinks.Add(new McpBacklink { TaskId = source.Id, Title = source.Title });
                    }
                }
            }

            return response;
        }

        public async Task<McpCreateResponse> CreateNote(string title, string body, string parentId)
        {
            string parent = NullIfEmpty(parentId);
            if (parent != null)
            {
                RequireActiveNote(parent);
            }

            Note note = new Note
            {
                Id = Guid.CreateVersion7().ToString(),
                ParentId = parent,
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                Position = FractionalIndex.Between(_notes.GetMaxChildPosition(parent), null),
                Type = NoteType.Normal,
            };
            _notes.Save(note);
            await NotifyWrite();
            return new McpCreateResponse { Id = note.Id };
        }

        public async Task<McpSaveResponse> UpdateNote(string noteId, string title, string body)
        {
            Note note = RequireActiveNote(noteId);
            if (title != null)
            {
                note.Title = title;
            }
            if (body != null)
            {
                note.Body = body;
            }
            _notes.Save(note);
            await NotifyWrite();
            return new McpSaveResponse { UpdatedAt = note.UpdatedAt };
        }

        public async Task<McpSaveResponse> AppendToNote(string noteId, string markdown)
        {
            Note note = RequireActiveNote(noteId);
            string text = markdown ?? string.Empty;
            note.Body = note.Body.Length > 0 ? note.Body.TrimEnd('\n') + "\n\n" + text : text;
            _notes.Save(note);
            await NotifyWrite();
            return new McpSaveResponse { UpdatedAt = note.UpdatedAt };
        }

        public McpBoardsResponse ListBoards()
        {
            McpBoardsResponse response = new McpBoardsResponse();

            foreach (Board board in _boards.GetActive())
            {
                McpBoardSummary summary = new McpBoardSummary { Id = board.Id, Name = board.Name };
                foreach (BoardColumn column in _columns.GetActiveForBoard(board.Id))
                {
                    summary.Columns.Add(new McpColumnSummary { Id = column.Id, Name = column.Name });
                }
                response.Boards.Add(summary);
            }

            return response;
        }

        public McpBoardResponse GetBoard(string boardId)
        {
            Board board = RequireBoard(boardId);
            McpBoardResponse response = new McpBoardResponse { Id = board.Id, Name = board.Name };
            Dictionary<string, McpColumnTasks> columnsById = new Dictionary<string, McpColumnTasks>();

            foreach (BoardColumn column in _columns.GetActiveForBoard(boardId))
            {
                McpColumnTasks columnTasks = new McpColumnTasks { Id = column.Id, Name = column.Name };
                columnsById[column.Id] = columnTasks;
                response.Columns.Add(columnTasks);
            }

            foreach (TaskItem task in _tasks.GetActiveForBoard(boardId))
            {
                if (columnsById.TryGetValue(task.ColumnId, out McpColumnTasks columnTasks))
                {
                    columnTasks.Tasks.Add(new McpTaskSummary { Id = task.Id, Title = task.Title });
                }
            }

            return response;
        }

        public McpTaskResponse GetTask(string taskId)
        {
            TaskItem task = RequireTask(taskId);
            BoardColumn column = _columns.Get(task.ColumnId);
            McpTaskResponse response = new McpTaskResponse
            {
                Id = task.Id,
                BoardId = column?.BoardId,
                ColumnId = task.ColumnId,
                Title = task.Title,
                Body = task.Body,
                CreatedAt = task.CreatedAt,
                UpdatedAt = task.UpdatedAt,
            };

            foreach (Attachment attachment in _attachments.GetForTask(taskId))
            {
                response.Attachments.Add(ToAttachmentInfo(attachment));
            }

            foreach (string noteId in task.NoteIds)
            {
                Note note = _notes.Get(noteId);
                if (note != null)
                {
                    response.LinkedNotes.Add(new McpLinkedNote { Id = note.Id, Title = note.Title });
                }
            }

            return response;
        }

        public async Task<McpCreateResponse> CreateTask(string columnId, string title, string body, string noteIds)
        {
            RequireColumn(columnId);
            TaskItem task = new TaskItem
            {
                Id = Guid.CreateVersion7().ToString(),
                ColumnId = columnId,
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                Position = FractionalIndex.Between(_tasks.GetMaxPosition(columnId), null),
                NoteIds = ParseNoteIds(noteIds),
            };
            _tasks.Save(task);
            await NotifyWrite();
            return new McpCreateResponse { Id = task.Id };
        }

        public async Task<McpSaveResponse> UpdateTask(string taskId, string title, string body)
        {
            TaskItem task = RequireTask(taskId);
            if (title != null)
            {
                task.Title = title;
            }
            if (body != null)
            {
                task.Body = body;
            }
            _tasks.Save(task);
            await NotifyWrite();
            return new McpSaveResponse { UpdatedAt = task.UpdatedAt };
        }

        public async Task<McpSaveResponse> MoveTask(string taskId, string columnId, int index)
        {
            TaskItem task = RequireTask(taskId);
            RequireColumn(columnId);

            List<TaskItem> targets = new List<TaskItem>();
            foreach (TaskItem candidate in _tasks.GetForColumn(columnId))
            {
                if (!candidate.Deleted && candidate.Id != taskId)
                {
                    targets.Add(candidate);
                }
            }

            int slot = index < 0 || index > targets.Count ? targets.Count : index;
            string lower = slot > 0 ? targets[slot - 1].Position : null;
            string upper = slot < targets.Count ? targets[slot].Position : null;
            string position = FractionalIndex.Between(lower, upper);
            while (_tasks.PositionExists(columnId, position))
            {
                position = FractionalIndex.Between(position, upper);
            }

            task.ColumnId = columnId;
            task.Position = position;
            _tasks.Save(task);
            await NotifyWrite();
            return new McpSaveResponse { UpdatedAt = task.UpdatedAt };
        }

        public async Task<McpSaveResponse> LinkNoteToTask(string taskId, string noteId)
        {
            TaskItem task = RequireTask(taskId);
            RequireActiveNote(noteId);

            // Additive by design (features/mcp.md) - re-linking an existing note is a
            // no-op instead of an error so agents need no read-first check.
            if (!task.NoteIds.Contains(noteId))
            {
                task.NoteIds.Add(noteId);
                _tasks.Save(task);
                await NotifyWrite();
            }

            return new McpSaveResponse { UpdatedAt = task.UpdatedAt };
        }

        public McpTemplatesResponse ListTemplates()
        {
            McpTemplatesResponse response = new McpTemplatesResponse();
            HashSet<string> templateIds = new HashSet<string>();
            List<Note> templates = new List<Note>();

            foreach (Note note in _notes.GetAll())
            {
                if (!note.Deleted && note.Type == NoteType.Template)
                {
                    templates.Add(note);
                    templateIds.Add(note.Id);
                }
            }

            foreach (Note note in templates)
            {
                if (note.ParentId == null || !templateIds.Contains(note.ParentId))
                {
                    response.Templates.Add(new McpTemplateSummary { Id = note.Id, Title = note.Title });
                }
            }

            return response;
        }

        public async Task<McpCreateResponse> CreateTemplate(string title, string body)
        {
            Note note = new Note
            {
                Id = Guid.CreateVersion7().ToString(),
                ParentId = null,
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                Position = FractionalIndex.Between(_notes.GetMaxChildPosition(null), null),
                Type = NoteType.Template,
            };
            _notes.Save(note);
            await NotifyWrite();
            return new McpCreateResponse { Id = note.Id };
        }

        public async Task<McpCreateResponse> CreateNoteFromTemplate(string templateId, string title, string parentId)
        {
            Note template = RequireActiveNote(templateId);
            if (template.Type != NoteType.Template)
            {
                throw new InvalidOperationException($"Note '{templateId}' is not a template.");
            }

            string parent = NullIfEmpty(parentId);
            if (parent != null)
            {
                RequireActiveNote(parent);
            }

            string position = FractionalIndex.Between(_notes.GetMaxChildPosition(parent), null);
            string rootId = _notes.InstantiateTemplate(templateId, title ?? string.Empty, parent, position);
            await NotifyWrite();
            return new McpCreateResponse { Id = rootId };
        }

        public McpAttachmentData GetAttachment(string attachmentId)
        {
            Attachment attachment = _attachments.Get(attachmentId);
            if (attachment == null)
            {
                throw new InvalidOperationException($"Attachment '{attachmentId}' does not exist.");
            }

            byte[] data = _attachments.GetBlob(attachmentId);
            if (data == null)
            {
                throw new InvalidOperationException($"Attachment '{attachmentId}' has no content here yet - it has not finished syncing.");
            }

            return new McpAttachmentData
            {
                Id = attachment.Id,
                Filename = attachment.Filename,
                MimeType = attachment.MimeType,
                Data = data,
            };
        }

        public async Task<McpCreateResponse> AddAttachment(string filename, string mimeType, string dataBase64, string noteId, string taskId)
        {
            string note = NullIfEmpty(noteId);
            string task = NullIfEmpty(taskId);
            if ((note == null) == (task == null))
            {
                throw new InvalidOperationException("Provide exactly one of noteId or taskId.");
            }

            if (note != null)
            {
                RequireActiveNote(note);
            }
            else
            {
                RequireTask(task);
            }

            byte[] data;
            try
            {
                data = Convert.FromBase64String(dataBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("dataBase64 is not valid base64.");
            }

            if (data.LongLength > MaxAttachmentBytes)
            {
                throw new InvalidOperationException("Attachment exceeds the 100 MB size limit.");
            }

            Attachment attachment = new Attachment
            {
                Id = Guid.CreateVersion7().ToString(),
                NoteId = note,
                TaskId = task,
                Filename = string.IsNullOrEmpty(filename) ? "attachment" : filename,
                MimeType = string.IsNullOrEmpty(mimeType) ? "application/octet-stream" : mimeType,
                SizeBytes = data.LongLength,
            };
            _attachments.Save(attachment);
            _attachments.SaveBlob(attachment.Id, data);
            await NotifyWrite();
            return new McpCreateResponse { Id = attachment.Id };
        }

        private async Task NotifyWrite()
        {
            if (_afterWrite != null)
            {
                await _afterWrite();
            }
        }

        private Dictionary<string, Note> GetNotesById()
        {
            Dictionary<string, Note> notesById = new Dictionary<string, Note>();
            foreach (Note note in _notes.GetAll())
            {
                notesById[note.Id] = note;
            }
            return notesById;
        }

        private static string BuildBreadcrumb(Dictionary<string, Note> notesById, string noteId)
        {
            List<string> titles = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            string current = notesById.TryGetValue(noteId, out Note start) ? start.ParentId : null;

            while (current != null && seen.Add(current) && notesById.TryGetValue(current, out Note ancestor))
            {
                titles.Insert(0, ancestor.Title);
                current = ancestor.ParentId;
            }

            return string.Join(" › ", titles);
        }

        private static McpAttachmentInfo ToAttachmentInfo(Attachment attachment)
        {
            return new McpAttachmentInfo
            {
                Id = attachment.Id,
                Filename = attachment.Filename,
                MimeType = attachment.MimeType,
                SizeBytes = attachment.SizeBytes,
            };
        }

        private List<string> ParseNoteIds(string noteIds)
        {
            List<string> ids = new List<string>();
            foreach (string part in (noteIds ?? string.Empty).Split(','))
            {
                string id = part.Trim();
                if (id.Length > 0 && !ids.Contains(id))
                {
                    RequireActiveNote(id);
                    ids.Add(id);
                }
            }
            return ids;
        }

        private Note RequireNote(string noteId)
        {
            Note note = _notes.Get(noteId);
            if (note == null)
            {
                throw new InvalidOperationException($"Note '{noteId}' does not exist.");
            }
            return note;
        }

        private Note RequireActiveNote(string noteId)
        {
            Note note = RequireNote(noteId);
            if (note.Deleted)
            {
                throw new InvalidOperationException($"Note '{noteId}' is in the trash.");
            }
            return note;
        }

        private Board RequireBoard(string boardId)
        {
            Board board = _boards.Get(boardId);
            if (board == null || board.Deleted)
            {
                throw new InvalidOperationException($"Board '{boardId}' does not exist.");
            }
            return board;
        }

        private BoardColumn RequireColumn(string columnId)
        {
            BoardColumn column = _columns.Get(columnId);
            if (column == null || column.Deleted)
            {
                throw new InvalidOperationException($"Column '{columnId}' does not exist.");
            }
            return column;
        }

        private TaskItem RequireTask(string taskId)
        {
            TaskItem task = _tasks.Get(taskId);
            if (task == null || task.Deleted)
            {
                throw new InvalidOperationException($"Task '{taskId}' does not exist.");
            }
            return task;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
