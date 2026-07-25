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
        private const int DefaultSearchLimit = 20;

        private readonly Func<Task> _afterWrite;
        private readonly NoteRepository _notes;
        private readonly BoardRepository _boards;
        private readonly BoardColumnRepository _columns;
        private readonly TaskRepository _tasks;
        private readonly AttachmentRepository _attachments;
        private readonly SearchRepository _search;

        public McpToolService(ConnectionManager connectionManager, string deviceId, Func<Task> afterWrite = null, int historyRetention = 50)
        {
            _afterWrite = afterWrite;
            _notes = new NoteRepository(connectionManager, deviceId, historyRetention);
            _boards = new BoardRepository(connectionManager, deviceId, historyRetention);
            _columns = new BoardColumnRepository(connectionManager, deviceId, historyRetention);
            _tasks = new TaskRepository(connectionManager, deviceId, historyRetention);
            _attachments = new AttachmentRepository(connectionManager, deviceId, historyRetention);
            _search = new SearchRepository(connectionManager);
        }

        // limit caps each category separately (FTS ranks are not comparable across
        // notes/tasks/boards, so there is no meaningful way to merge and then trim).
        public McpSearchResponse Search(string query, int limit, string type)
        {
            string scope = (type ?? "all").Trim().ToLowerInvariant();
            if (scope != "all" && scope != "notes" && scope != "tasks" && scope != "boards")
            {
                throw new InvalidOperationException("type must be one of: all, notes, tasks, boards.");
            }

            int cap = limit > 0 ? limit : DefaultSearchLimit;
            McpSearchResponse response = new McpSearchResponse();

            if (scope == "all" || scope == "notes")
            {
                Dictionary<string, Note> notesById = GetNotesById();
                foreach (SearchResult hit in _search.SearchNotes(query, false, cap))
                {
                    notesById.TryGetValue(hit.Id, out Note note);
                    response.Notes.Add(new McpNoteHit
                    {
                        Id = hit.Id,
                        Title = hit.Title,
                        Breadcrumb = BuildBreadcrumb(notesById, hit.Id),
                        Snippet = hit.Snippet,
                        UpdatedAt = note?.UpdatedAt,
                    });
                }
            }

            if (scope == "all" || scope == "tasks")
            {
                foreach (TaskSearchResult hit in _search.SearchTasksWithContext(query, cap))
                {
                    response.Tasks.Add(new McpTaskHit
                    {
                        Id = hit.Id,
                        Title = hit.Title,
                        Breadcrumb = $"{hit.BoardName} › {hit.ColumnName}",
                        Snippet = hit.Snippet,
                        UpdatedAt = hit.UpdatedAt,
                    });
                }
            }

            if (scope == "all" || scope == "boards")
            {
                foreach (SearchResult hit in _search.SearchBoards(query, cap))
                {
                    response.Boards.Add(new McpBoardHit { Id = hit.Id, Name = hit.Title });
                }
            }

            return response;
        }

        // parentId returns that note's children as the top level (symmetric with the
        // root listing); depth 0 means every level. Both exist so an agent can walk a
        // large tree in pieces instead of pulling all of it into context.
        public McpNoteTreeResponse ListNoteTree(string parentId, int depth)
        {
            if (!string.IsNullOrEmpty(parentId))
            {
                RequireActiveNote(parentId);
            }

            HashSet<string> activeIds = new HashSet<string>();
            List<Note> active = new List<Note>();

            foreach (Note note in _notes.GetAll())
            {
                if (!note.Deleted && note.Type == NoteType.Normal)
                {
                    active.Add(note);
                    activeIds.Add(note.Id);
                }
            }

            Dictionary<string, List<Note>> childrenByParent = new Dictionary<string, List<Note>>();
            foreach (Note note in active)
            {
                // A note whose parent is trashed or a template surfaces at the root
                // rather than disappearing.
                string key = note.ParentId != null && activeIds.Contains(note.ParentId) ? note.ParentId : string.Empty;
                if (!childrenByParent.TryGetValue(key, out List<Note> siblings))
                {
                    siblings = new List<Note>();
                    childrenByParent[key] = siblings;
                }
                siblings.Add(note);
            }

            McpNoteTreeResponse response = new McpNoteTreeResponse();
            if (childrenByParent.TryGetValue(parentId ?? string.Empty, out List<Note> roots))
            {
                AppendTreeLevel(response.Notes, roots, childrenByParent, depth);
            }

            return response;
        }

        private static void AppendTreeLevel(
            List<McpTreeNode> target,
            List<Note> notes,
            Dictionary<string, List<Note>> childrenByParent,
            int remainingDepth)
        {
            foreach (Note note in notes)
            {
                McpTreeNode node = new McpTreeNode { Id = note.Id, Title = note.Title, UpdatedAt = note.UpdatedAt };
                target.Add(node);

                if (remainingDepth != 1 && childrenByParent.TryGetValue(note.Id, out List<Note> children))
                {
                    AppendTreeLevel(node.Children, children, childrenByParent, remainingDepth > 0 ? remainingDepth - 1 : 0);
                }
            }
        }

        // Notes and tasks share one time-ordered list: without this there is no
        // browse-by-time path at all (search needs a query), so "what changed
        // recently" would mean fetching every item to read its timestamp.
        public McpRecentResponse ListRecent(int limit, string type)
        {
            string scope = (type ?? "all").Trim().ToLowerInvariant();
            if (scope != "all" && scope != "notes" && scope != "tasks")
            {
                throw new InvalidOperationException("type must be one of: all, notes, tasks.");
            }

            int cap = limit > 0 ? limit : 20;
            List<McpRecentItem> items = new List<McpRecentItem>();

            if (scope != "tasks")
            {
                Dictionary<string, Note> notesById = GetNotesById();
                foreach (Note note in notesById.Values)
                {
                    if (!note.Deleted && note.Type == NoteType.Normal)
                    {
                        items.Add(new McpRecentItem
                        {
                            Type = "note",
                            Id = note.Id,
                            Title = note.Title,
                            Breadcrumb = BuildBreadcrumb(notesById, note.Id),
                            UpdatedAt = note.UpdatedAt,
                        });
                    }
                }
            }

            if (scope != "notes")
            {
                foreach (Board board in _boards.GetActive())
                {
                    Dictionary<string, string> columnNames = new Dictionary<string, string>();
                    foreach (BoardColumn column in _columns.GetActiveForBoard(board.Id))
                    {
                        columnNames[column.Id] = column.Name;
                    }

                    foreach (TaskItem task in _tasks.GetActiveForBoard(board.Id))
                    {
                        if (columnNames.TryGetValue(task.ColumnId, out string columnName))
                        {
                            items.Add(new McpRecentItem
                            {
                                Type = "task",
                                Id = task.Id,
                                Title = task.Title,
                                Breadcrumb = $"{board.Name} › {columnName}",
                                UpdatedAt = task.UpdatedAt,
                            });
                        }
                    }
                }
            }

            // Timestamps are fixed-format ISO-8601 UTC from Timestamps.Now, so an
            // ordinal string sort is a chronological sort - no parsing needed.
            items.Sort((left, right) => string.CompareOrdinal(right.UpdatedAt, left.UpdatedAt));

            McpRecentResponse response = new McpRecentResponse();
            foreach (McpRecentItem item in items)
            {
                if (response.Items.Count < cap)
                {
                    response.Items.Add(item);
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

            // Both directions (body mentions and tasks' linked-notes lists), and it
            // already drops trashed notes and deleted tasks - the rest of the contract
            // never surfaces trashed items either.
            foreach (NoteBacklink source in _notes.GetBacklinkSources(noteId))
            {
                response.Backlinks.Add(new McpBacklink
                {
                    NoteId = source.NoteId,
                    TaskId = source.TaskId,
                    Title = source.Title,
                    Via = source.Via,
                });
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
                    columnTasks.Tasks.Add(new McpTaskSummary { Id = task.Id, Title = task.Title, UpdatedAt = task.UpdatedAt });
                }
            }

            return response;
        }

        public McpTaskResponse GetTask(string taskId)
        {
            TaskItem task = RequireTask(taskId);
            BoardColumn column = _columns.Get(task.ColumnId);

            // Names as well as ids: without them "move this to Done" costs an extra
            // list_boards round trip just to map a column name onto its id.
            Board board = column?.BoardId == null ? null : _boards.Get(column.BoardId);
            McpTaskResponse response = new McpTaskResponse
            {
                Id = task.Id,
                BoardId = column?.BoardId,
                BoardName = board?.Name,
                ColumnId = task.ColumnId,
                ColumnName = column?.Name,
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

        public async Task<McpCreateResponse> CreateTask(string columnId, string title, string body, string[] noteIds)
        {
            RequireColumn(columnId);
            TaskItem task = new TaskItem
            {
                Id = Guid.CreateVersion7().ToString(),
                ColumnId = columnId,
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                Position = FractionalIndex.Between(_tasks.GetMaxPosition(columnId), null),
                NoteIds = ResolveNoteIds(noteIds),
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

        public async Task<McpMoveResponse> MoveTask(string taskId, string columnId, int index)
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
            return new McpMoveResponse { UpdatedAt = task.UpdatedAt, ColumnId = columnId, Index = slot };
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
                    response.Templates.Add(new McpTemplateSummary { Id = note.Id, Title = note.Title, UpdatedAt = note.UpdatedAt });
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

        // Trims and de-duplicates because agents still send sloppy arrays (blank
        // entries, the same id twice).
        private List<string> ResolveNoteIds(string[] noteIds)
        {
            List<string> ids = new List<string>();
            foreach (string entry in noteIds ?? Array.Empty<string>())
            {
                string id = (entry ?? string.Empty).Trim();
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
