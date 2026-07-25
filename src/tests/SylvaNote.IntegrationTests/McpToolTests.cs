using System;
using System.Linq;
using System.Threading.Tasks;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Mcp;
using SylvaNote.Core.Mcp.Contracts;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class McpToolTests : IDisposable
    {
        private readonly TestDb _db;
        private readonly McpToolService _tools;
        private int _writeNotifications;

        public McpToolTests()
        {
            _db = new TestDb();
            _tools = new McpToolService(_db.ConnectionManager, "mcp-test", () =>
            {
                _writeNotifications++;
                return Task.CompletedTask;
            });
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task CreateNoteAppearsInTreeAndGet()
        {
            McpCreateResponse parent = await _tools.CreateNote("Parent", "parent body", null);
            McpCreateResponse child = await _tools.CreateNote("Child", "child body", parent.Id);

            McpNoteTreeResponse tree = _tools.ListNoteTree(null, 0);
            McpTreeNode root = Assert.Single(tree.Notes);
            Assert.Equal("Parent", root.Title);
            Assert.Equal("Child", Assert.Single(root.Children).Title);

            McpNoteResponse note = _tools.GetNote(child.Id);
            Assert.Equal("child body", note.Body);
            Assert.Equal(parent.Id, note.ParentId);
            Assert.Equal(2, _writeNotifications);
        }

        [Fact]
        public async Task CreateNoteUnderMissingParentFails()
        {
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.CreateNote("Orphan", null, Items.NewId()));
            Assert.Contains("does not exist", ex.Message);
            Assert.Equal(0, _writeNotifications);
        }

        [Fact]
        public async Task UpdateNoteKeepsOmittedFields()
        {
            McpCreateResponse created = await _tools.CreateNote("Title", "body", null);

            await _tools.UpdateNote(created.Id, null, "new body");
            McpNoteResponse afterBody = _tools.GetNote(created.Id);
            Assert.Equal("Title", afterBody.Title);
            Assert.Equal("new body", afterBody.Body);

            await _tools.UpdateNote(created.Id, "New title", null);
            McpNoteResponse afterTitle = _tools.GetNote(created.Id);
            Assert.Equal("New title", afterTitle.Title);
            Assert.Equal("new body", afterTitle.Body);
        }

        [Fact]
        public async Task AppendToNoteSeparatesWithABlankLine()
        {
            McpCreateResponse created = await _tools.CreateNote("Log", "first entry", null);
            await _tools.AppendToNote(created.Id, "second entry");

            Assert.Equal("first entry\n\nsecond entry", _tools.GetNote(created.Id).Body);

            McpCreateResponse empty = await _tools.CreateNote("Empty", null, null);
            await _tools.AppendToNote(empty.Id, "only entry");
            Assert.Equal("only entry", _tools.GetNote(empty.Id).Body);
        }

        [Fact]
        public async Task AppendToTrashedNoteFails()
        {
            McpCreateResponse created = await _tools.CreateNote("Doomed", null, null);
            _db.Notes.TrashSubtree(created.Id);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.AppendToNote(created.Id, "text"));
            Assert.Contains("trash", ex.Message);
        }

        // A trashed note stays readable by id (flagged deleted) but refuses every write,
        // so an agent can tell "gone" from "in the trash" and gets a clear reason.
        [Fact]
        public async Task TrashedNotesAreReadableButRefuseEveryWrite()
        {
            McpCreateResponse note = await _tools.CreateNote("Doomed", "body", null);
            string data = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            _db.Notes.TrashSubtree(note.Id);

            McpNoteResponse read = _tools.GetNote(note.Id);
            Assert.True(read.Deleted);
            Assert.Equal("body", read.Body);
            Assert.DoesNotContain(_tools.ListRecent(20, "notes").Items, item => item.Id == note.Id);
            Assert.Empty(_tools.Search("Doomed", 20, "notes").Notes);

            Assert.Contains("trash", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.UpdateNote(note.Id, "New title", null))).Message);
            Assert.Contains("trash", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.AddAttachment("pic.png", "image/png", data, note.Id, null))).Message);
            Assert.Contains("trash", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.CreateNote("Child", null, note.Id))).Message);
            Assert.Contains("trash", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.CreateNoteFromTemplate(note.Id, "Copy", null))).Message);
        }

        [Fact]
        public async Task TreeExcludesTrashedNotesAndTemplates()
        {
            McpCreateResponse kept = await _tools.CreateNote("Kept", null, null);
            McpCreateResponse trashed = await _tools.CreateNote("Trashed", null, null);
            _db.Notes.TrashSubtree(trashed.Id);
            await _tools.CreateTemplate("Template", "template body");

            McpNoteTreeResponse tree = _tools.ListNoteTree(null, 0);
            Assert.Equal(kept.Id, Assert.Single(tree.Notes).Id);
        }

        [Fact]
        public async Task ListNoteTreeScopesToASubtreeAndDepth()
        {
            McpCreateResponse root = await _tools.CreateNote("Root", null, null);
            McpCreateResponse child = await _tools.CreateNote("Child", null, root.Id);
            McpCreateResponse grandchild = await _tools.CreateNote("Grandchild", null, child.Id);
            await _tools.CreateNote("Unrelated", null, null);

            McpNoteTreeResponse subtree = _tools.ListNoteTree(root.Id, 0);
            McpTreeNode childNode = Assert.Single(subtree.Notes);
            Assert.Equal(child.Id, childNode.Id);
            Assert.Equal(grandchild.Id, Assert.Single(childNode.Children).Id);

            McpNoteTreeResponse oneLevel = _tools.ListNoteTree(root.Id, 1);
            Assert.Empty(Assert.Single(oneLevel.Notes).Children);

            McpNoteTreeResponse depthFromRoot = _tools.ListNoteTree(null, 1);
            Assert.Equal(2, depthFromRoot.Notes.Count);
            Assert.All(depthFromRoot.Notes, node => Assert.Empty(node.Children));

            Assert.Empty(_tools.ListNoteTree(grandchild.Id, 0).Notes);
            Assert.Throws<InvalidOperationException>(() => _tools.ListNoteTree("no-such-note", 0));
        }

        [Fact]
        public async Task SearchReturnsBreadcrumbsForNotesAndTasks()
        {
            McpCreateResponse parent = await _tools.CreateNote("Projects", null, null);
            await _tools.CreateNote("Sylva ideas", "the flywheel concept", parent.Id);

            Board board = Items.Board("Roadmap");
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Doing");
            _db.Columns.Save(column);
            await _tools.CreateTask(column.Id, "Ship flywheel", "the flywheel task", null);

            McpSearchResponse results = _tools.Search("flywheel", 20, "all");
            McpNoteHit noteHit = Assert.Single(results.Notes);
            Assert.Equal("Projects", noteHit.Breadcrumb);
            Assert.Contains("flywheel", noteHit.Snippet);
            Assert.False(string.IsNullOrEmpty(noteHit.UpdatedAt));
            McpTaskHit taskHit = Assert.Single(results.Tasks);
            Assert.Equal("Roadmap › Doing", taskHit.Breadcrumb);
            Assert.False(string.IsNullOrEmpty(taskHit.UpdatedAt));

            McpSearchResponse boards = _tools.Search("roadmap", 20, "all");
            Assert.Equal(board.Id, Assert.Single(boards.Boards).Id);
        }

        [Fact]
        public async Task SearchHonoursLimitAndTypeScope()
        {
            await _tools.CreateNote("Alpha one", "shared marker", null);
            await _tools.CreateNote("Alpha two", "shared marker", null);
            Board board = Items.Board("Marker board");
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Doing");
            _db.Columns.Save(column);
            await _tools.CreateTask(column.Id, "Marker task", "shared marker", null);

            Assert.Equal(2, _tools.Search("marker", 20, "all").Notes.Count);
            Assert.Single(_tools.Search("marker", 1, "all").Notes);

            McpSearchResponse notesOnly = _tools.Search("marker", 20, "notes");
            Assert.NotEmpty(notesOnly.Notes);
            Assert.Empty(notesOnly.Tasks);
            Assert.Empty(notesOnly.Boards);

            McpSearchResponse tasksOnly = _tools.Search("marker", 20, "tasks");
            Assert.Empty(tasksOnly.Notes);
            Assert.NotEmpty(tasksOnly.Tasks);

            McpSearchResponse boardsOnly = _tools.Search("marker", 20, "boards");
            Assert.Empty(boardsOnly.Notes);
            Assert.NotEmpty(boardsOnly.Boards);

            Assert.Throws<InvalidOperationException>(() => _tools.Search("marker", 20, "bogus"));
        }

        [Fact]
        public async Task GetNoteIncludesAttachmentsAndBacklinks()
        {
            McpCreateResponse target = await _tools.CreateNote("Target", null, null);
            await _tools.CreateNote("Linker", $"see note://{target.Id}", null);

            Board board = Items.Board();
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            _db.Columns.Save(column);
            McpCreateResponse task = await _tools.CreateTask(column.Id, "Task linker", $"also note://{target.Id}", null);

            string data = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            await _tools.AddAttachment("pic.png", "image/png", data, target.Id, null);

            McpNoteResponse note = _tools.GetNote(target.Id);
            McpAttachmentInfo attachment = Assert.Single(note.Attachments);
            Assert.Equal("pic.png", attachment.Filename);
            Assert.Equal(3, attachment.SizeBytes);

            // A task linked to the note without mentioning it also backlinks, marked
            // via "link" instead of "body".
            McpCreateResponse linkedTask = await _tools.CreateTask(column.Id, "Task holder", "no mention here",
                new[] { target.Id });

            note = _tools.GetNote(target.Id);
            Assert.Equal(3, note.Backlinks.Count);
            Assert.Contains(note.Backlinks, b => b.NoteId != null && b.Title == "Linker" && b.Via == BacklinkVia.Body);
            Assert.Contains(note.Backlinks, b => b.TaskId == task.Id && b.Via == BacklinkVia.Body);
            Assert.Contains(note.Backlinks, b => b.TaskId == linkedTask.Id && b.Via == BacklinkVia.Link);
        }

        [Fact]
        public async Task BoardAndTaskFlowRoundTrips()
        {
            Board board = Items.Board("Personal");
            _db.Boards.Save(board);
            BoardColumn todo = Items.Column(board.Id, "Todo");
            todo.Position = "F";
            _db.Columns.Save(todo);
            BoardColumn done = Items.Column(board.Id, "Done");
            done.Position = "V";
            _db.Columns.Save(done);

            McpBoardsResponse boards = _tools.ListBoards();
            McpBoardSummary summary = Assert.Single(boards.Boards);
            Assert.Equal(new[] { "Todo", "Done" }, summary.Columns.Select(c => c.Name).ToArray());

            McpCreateResponse note = await _tools.CreateNote("Spec", null, null);
            McpCreateResponse first = await _tools.CreateTask(todo.Id, "First", "body", new[] { note.Id });
            McpCreateResponse second = await _tools.CreateTask(todo.Id, "Second", null, null);

            McpTaskResponse task = _tools.GetTask(first.Id);
            Assert.Equal(board.Id, task.BoardId);
            Assert.Equal("Personal", task.BoardName);
            Assert.Equal(todo.Id, task.ColumnId);
            Assert.Equal("Todo", task.ColumnName);
            Assert.Equal("Spec", Assert.Single(task.LinkedNotes).Title);

            // Move "Second" to the front of Todo, then "First" to Done.
            McpMoveResponse toFront = await _tools.MoveTask(second.Id, todo.Id, 0);
            Assert.Equal(todo.Id, toFront.ColumnId);
            Assert.Equal(0, toFront.Index);

            McpMoveResponse toDone = await _tools.MoveTask(first.Id, done.Id, -1);
            Assert.Equal(done.Id, toDone.ColumnId);
            Assert.Equal(0, toDone.Index);

            McpBoardResponse detail = _tools.GetBoard(board.Id);
            McpColumnTasks todoTasks = detail.Columns.First(c => c.Id == todo.Id);
            McpColumnTasks doneTasks = detail.Columns.First(c => c.Id == done.Id);
            Assert.Equal("Second", Assert.Single(todoTasks.Tasks).Title);
            Assert.Equal("First", Assert.Single(doneTasks.Tasks).Title);
        }

        [Fact]
        public async Task MoveTaskReportsTheClampedIndex()
        {
            Board board = Items.Board();
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Todo");
            _db.Columns.Save(column);
            await _tools.CreateTask(column.Id, "First", null, null);
            await _tools.CreateTask(column.Id, "Second", null, null);
            McpCreateResponse third = await _tools.CreateTask(column.Id, "Third", null, null);

            // Asking for index 99 in a column of three lands at the end, not at 99.
            McpMoveResponse moved = await _tools.MoveTask(third.Id, column.Id, 99);
            Assert.Equal(column.Id, moved.ColumnId);
            Assert.Equal(2, moved.Index);
            Assert.False(string.IsNullOrEmpty(moved.UpdatedAt));
        }

        [Fact]
        public async Task LinkNoteToTaskIsAdditiveAndIdempotent()
        {
            Board board = Items.Board();
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            _db.Columns.Save(column);
            McpCreateResponse noteA = await _tools.CreateNote("A", null, null);
            McpCreateResponse noteB = await _tools.CreateNote("B", null, null);
            McpCreateResponse task = await _tools.CreateTask(column.Id, "Task", null, new[] { noteA.Id });

            await _tools.LinkNoteToTask(task.Id, noteB.Id);
            await _tools.LinkNoteToTask(task.Id, noteB.Id);

            McpTaskResponse detail = _tools.GetTask(task.Id);
            Assert.Equal(2, detail.LinkedNotes.Count);
        }

        [Fact]
        public async Task CreateTaskLinksEveryNoteIdInTheArray()
        {
            Board board = Items.Board();
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            _db.Columns.Save(column);
            McpCreateResponse noteA = await _tools.CreateNote("A", null, null);
            McpCreateResponse noteB = await _tools.CreateNote("B", null, null);

            McpCreateResponse task = await _tools.CreateTask(
                column.Id,
                "Task",
                null,
                new[] { noteA.Id, noteB.Id, noteA.Id, "  ", null });

            // Links read back ORDER BY note_id, and two UUIDv7 ids minted in the same
            // millisecond sort randomly against each other - compare as a set.
            McpTaskResponse detail = _tools.GetTask(task.Id);
            Assert.Equal(
                new[] { noteA.Id, noteB.Id }.OrderBy(id => id, StringComparer.Ordinal),
                detail.LinkedNotes.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal));
        }

        [Fact]
        public async Task ListRecentMergesNotesAndTasksNewestFirst()
        {
            Board board = Items.Board();
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Todo");
            _db.Columns.Save(column);

            McpCreateResponse older = await _tools.CreateNote("Older note", null, null);
            McpCreateResponse task = await _tools.CreateTask(column.Id, "A task", null, null);
            McpCreateResponse newest = await _tools.CreateNote("Newest note", null, null);

            McpRecentResponse recent = _tools.ListRecent(20, "all");

            Assert.Equal(3, recent.Items.Count);
            Assert.Equal(newest.Id, recent.Items[0].Id);
            Assert.Equal("note", recent.Items[0].Type);
            Assert.Contains(recent.Items, item => item.Id == older.Id);

            McpRecentItem taskItem = Assert.Single(recent.Items, item => item.Type == "task");
            Assert.Equal(task.Id, taskItem.Id);
            Assert.Equal($"{board.Name} › Todo", taskItem.Breadcrumb);
            Assert.False(string.IsNullOrEmpty(taskItem.UpdatedAt));
        }

        [Fact]
        public async Task ListRecentHonoursLimitAndTypeScope()
        {
            Board board = Items.Board();
            _db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Todo");
            _db.Columns.Save(column);
            await _tools.CreateNote("A note", null, null);
            await _tools.CreateTask(column.Id, "A task", null, null);

            Assert.Single(_tools.ListRecent(1, "all").Items);
            Assert.All(_tools.ListRecent(20, "notes").Items, item => Assert.Equal("note", item.Type));
            Assert.All(_tools.ListRecent(20, "tasks").Items, item => Assert.Equal("task", item.Type));
            Assert.Throws<InvalidOperationException>(() => _tools.ListRecent(20, "bogus"));
        }

        [Fact]
        public async Task TemplatesListRootsAndInstantiate()
        {
            McpCreateResponse template = await _tools.CreateTemplate("Meeting", "## Agenda");
            McpCreateResponse childOfTemplate = await _tools.CreateNote("Child section", "child body", template.Id);
            Note child = _db.Notes.Get(childOfTemplate.Id);
            child.Type = NoteType.Template;
            _db.Notes.Save(child);

            McpTemplatesResponse templates = _tools.ListTemplates();
            Assert.Equal(template.Id, Assert.Single(templates.Templates).Id);

            McpCreateResponse parent = await _tools.CreateNote("Meetings", null, null);
            McpCreateResponse instantiated = await _tools.CreateNoteFromTemplate(template.Id, "Standup 7-24", parent.Id);

            McpNoteResponse root = _tools.GetNote(instantiated.Id);
            Assert.Equal("Standup 7-24", root.Title);
            Assert.Equal("## Agenda", root.Body);
            Assert.Equal(parent.Id, root.ParentId);
            Assert.NotEqual(template.Id, root.Id);

            McpNoteTreeResponse tree = _tools.ListNoteTree(null, 0);
            McpTreeNode meetings = tree.Notes.First(n => n.Id == parent.Id);
            McpTreeNode copy = Assert.Single(meetings.Children);
            Assert.Equal("Child section", Assert.Single(copy.Children).Title);
        }

        [Fact]
        public async Task CreateNoteFromTemplateRejectsANormalNote()
        {
            McpCreateResponse note = await _tools.CreateNote("Not a template", null, null);
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.CreateNoteFromTemplate(note.Id, "Copy", null));
            Assert.Contains("not a template", ex.Message);
        }

        [Fact]
        public async Task AttachmentRoundTripsThroughAddAndGet()
        {
            McpCreateResponse note = await _tools.CreateNote("Owner", null, null);
            byte[] bytes = new byte[] { 10, 20, 30, 40 };
            McpCreateResponse added = await _tools.AddAttachment("data.bin", "application/octet-stream", Convert.ToBase64String(bytes), note.Id, null);

            McpAttachmentData attachment = _tools.GetAttachment(added.Id);
            Assert.Equal("data.bin", attachment.Filename);
            Assert.Equal(bytes, attachment.Data);
        }

        [Fact]
        public async Task AddAttachmentRequiresExactlyOneOwner()
        {
            McpCreateResponse note = await _tools.CreateNote("Owner", null, null);

            InvalidOperationException none = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.AddAttachment("f", "text/plain", string.Empty, null, null));
            Assert.Contains("exactly one", none.Message);

            InvalidOperationException both = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _tools.AddAttachment("f", "text/plain", string.Empty, note.Id, Items.NewId()));
            Assert.Contains("exactly one", both.Message);
        }

        [Fact]
        public async Task WritesLandInTheOutbox()
        {
            await _tools.CreateNote("Synced", null, null);

            PendingChange pending = Assert.Single(_db.ChangeLog.GetPending());
            Assert.Equal("mcp-test", pending.Entry.DeviceId);
            Assert.Null(pending.Entry.Seq);
        }
    }
}
