using System;
using System.Collections.Generic;
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

            McpNoteTreeResponse tree = _tools.ListNoteTree();
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

        [Fact]
        public async Task TreeExcludesTrashedNotesAndTemplates()
        {
            McpCreateResponse kept = await _tools.CreateNote("Kept", null, null);
            McpCreateResponse trashed = await _tools.CreateNote("Trashed", null, null);
            _db.Notes.TrashSubtree(trashed.Id);
            await _tools.CreateTemplate("Template", "template body");

            McpNoteTreeResponse tree = _tools.ListNoteTree();
            Assert.Equal(kept.Id, Assert.Single(tree.Notes).Id);
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

            McpSearchResponse results = _tools.Search("flywheel");
            McpNoteHit noteHit = Assert.Single(results.Notes);
            Assert.Equal("Projects", noteHit.Breadcrumb);
            Assert.Contains("flywheel", noteHit.Snippet);
            McpTaskHit taskHit = Assert.Single(results.Tasks);
            Assert.Equal("Roadmap › Doing", taskHit.Breadcrumb);

            McpSearchResponse boards = _tools.Search("roadmap");
            Assert.Equal(board.Id, Assert.Single(boards.Boards).Id);
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

            Assert.Equal(2, note.Backlinks.Count);
            Assert.Contains(note.Backlinks, b => b.NoteId != null && b.Title == "Linker");
            Assert.Contains(note.Backlinks, b => b.TaskId == task.Id && b.Title == "Task linker");
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
            McpCreateResponse first = await _tools.CreateTask(todo.Id, "First", "body", note.Id);
            McpCreateResponse second = await _tools.CreateTask(todo.Id, "Second", null, null);

            McpTaskResponse task = _tools.GetTask(first.Id);
            Assert.Equal(board.Id, task.BoardId);
            Assert.Equal("Spec", Assert.Single(task.LinkedNotes).Title);

            // Move "Second" to the front of Todo, then "First" to Done.
            await _tools.MoveTask(second.Id, todo.Id, 0);
            await _tools.MoveTask(first.Id, done.Id, -1);

            McpBoardResponse detail = _tools.GetBoard(board.Id);
            McpColumnTasks todoTasks = detail.Columns.First(c => c.Id == todo.Id);
            McpColumnTasks doneTasks = detail.Columns.First(c => c.Id == done.Id);
            Assert.Equal("Second", Assert.Single(todoTasks.Tasks).Title);
            Assert.Equal("First", Assert.Single(doneTasks.Tasks).Title);
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
            McpCreateResponse task = await _tools.CreateTask(column.Id, "Task", null, noteA.Id);

            await _tools.LinkNoteToTask(task.Id, noteB.Id);
            await _tools.LinkNoteToTask(task.Id, noteB.Id);

            McpTaskResponse detail = _tools.GetTask(task.Id);
            Assert.Equal(2, detail.LinkedNotes.Count);
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

            McpNoteTreeResponse tree = _tools.ListNoteTree();
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
