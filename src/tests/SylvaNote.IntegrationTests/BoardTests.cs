using System.Collections.Generic;
using System.Linq;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Search;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class BoardTests
    {
        [Fact]
        public void DeleteCascade_Board_TombstonesColumnsAndTasks_WithOutboxEntryEach()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board();
            BoardColumn column = Items.Column(board.Id);
            TaskItem task = Items.Task(column.Id);
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Tasks.Save(task);

            db.Boards.DeleteCascade(board.Id);

            Assert.True(db.Boards.Get(board.Id).Deleted);
            Assert.True(db.Columns.Get(column.Id).Deleted);
            Assert.True(db.Tasks.Get(task.Id).Deleted);
            Assert.Equal(6, db.ChangeLog.GetPending().Count);
        }

        [Fact]
        public void DeleteCascade_Column_TombstonesOnlyItsTasks()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board();
            BoardColumn column = Items.Column(board.Id, "Doomed");
            BoardColumn other = Items.Column(board.Id, "Safe");
            other.Position = "W";
            TaskItem task = Items.Task(column.Id);
            TaskItem otherTask = Items.Task(other.Id);
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Columns.Save(other);
            db.Tasks.Save(task);
            db.Tasks.Save(otherTask);

            db.Columns.DeleteCascade(column.Id);

            Assert.True(db.Columns.Get(column.Id).Deleted);
            Assert.True(db.Tasks.Get(task.Id).Deleted);
            Assert.False(db.Columns.Get(other.Id).Deleted);
            Assert.False(db.Tasks.Get(otherTask.Id).Deleted);
            Assert.False(db.Boards.Get(board.Id).Deleted);
        }

        [Fact]
        public void DeleteCascade_Task_TombstonePayloadKeepsNoteLinks()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Linked");
            Board board = Items.Board();
            BoardColumn column = Items.Column(board.Id);
            TaskItem task = Items.Task(column.Id, noteIds: new List<string> { note.Id });
            db.Notes.Save(note);
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Tasks.Save(task);

            db.Tasks.Delete(task.Id);

            Assert.True(db.Tasks.Get(task.Id).Deleted);
            PendingChange tombstone = db.ChangeLog.GetPending()
                .Last(p => p.Entry.ItemType == ItemTypes.Task && p.Entry.ItemId == task.Id);
            Assert.Contains(note.Id, tombstone.Entry.Payload);
        }

        [Fact]
        public void GetActiveForBoard_ExcludesTombstonedRows()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board();
            BoardColumn column = Items.Column(board.Id, "Alive");
            BoardColumn deadColumn = Items.Column(board.Id, "Dead");
            deadColumn.Position = "W";
            TaskItem task = Items.Task(column.Id, "Alive task");
            TaskItem deadTask = Items.Task(column.Id, "Dead task");
            deadTask.Position = "W";
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Columns.Save(deadColumn);
            db.Tasks.Save(task);
            db.Tasks.Save(deadTask);

            db.Columns.DeleteCascade(deadColumn.Id);
            db.Tasks.Delete(deadTask.Id);

            List<BoardColumn> columns = db.Columns.GetActiveForBoard(board.Id);
            List<TaskItem> tasks = db.Tasks.GetActiveForBoard(board.Id);
            Assert.Single(columns);
            Assert.Equal(column.Id, columns[0].Id);
            Assert.Single(tasks);
            Assert.Equal(task.Id, tasks[0].Id);
        }

        [Fact]
        public void GetActive_ExcludesDeletedBoards()
        {
            using TestDb db = new TestDb();
            Board alive = Items.Board("Alive");
            Board dead = Items.Board("Dead");
            dead.Position = "W";
            db.Boards.Save(alive);
            db.Boards.Save(dead);

            db.Boards.DeleteCascade(dead.Id);

            List<Board> boards = db.Boards.GetActive();
            Assert.Single(boards);
            Assert.Equal(alive.Id, boards[0].Id);
        }

        [Fact]
        public void CountByTaskForBoard_CountsActiveAttachmentsOnly()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board();
            BoardColumn column = Items.Column(board.Id);
            TaskItem task = Items.Task(column.Id);
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Tasks.Save(task);

            Attachment first = Items.Attachment(taskId: task.Id, filename: "a.png");
            Attachment second = Items.Attachment(taskId: task.Id, filename: "b.png");
            Attachment removed = Items.Attachment(taskId: task.Id, filename: "c.png");
            db.Attachments.Save(first);
            db.Attachments.Save(second);
            db.Attachments.Save(removed);
            removed.Deleted = true;
            db.Attachments.Save(removed);

            Dictionary<string, int> counts = db.Attachments.CountByTaskForBoard(board.Id);
            Assert.Equal(2, counts[task.Id]);
        }

        [Fact]
        public void GetForTask_ReturnsActiveTaskAttachments()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board();
            BoardColumn column = Items.Column(board.Id);
            TaskItem task = Items.Task(column.Id);
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Tasks.Save(task);
            Attachment attachment = Items.Attachment(taskId: task.Id);
            db.Attachments.Save(attachment);

            List<Attachment> attachments = db.Attachments.GetForTask(task.Id);
            Assert.Single(attachments);
            Assert.Equal(attachment.Id, attachments[0].Id);
        }

        [Fact]
        public void SearchTasksWithContext_ReturnsBoardAndColumnNames()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board("Roadmap");
            BoardColumn column = Items.Column(board.Id, "Doing");
            TaskItem task = Items.Task(column.Id, "Ship kanban", "drag and drop everywhere");
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Tasks.Save(task);

            List<TaskSearchResult> results = db.Search.SearchTasksWithContext("kanban");

            Assert.Single(results);
            Assert.Equal(task.Id, results[0].Id);
            Assert.Equal("Doing", results[0].ColumnName);
            Assert.Equal("Roadmap", results[0].BoardName);
        }

        [Fact]
        public void SearchTasksWithContext_ExcludesTombstonedTasksAndBoards()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board("Roadmap");
            BoardColumn column = Items.Column(board.Id);
            TaskItem kept = Items.Task(column.Id, "kanban keep");
            TaskItem gone = Items.Task(column.Id, "kanban gone");
            gone.Position = "W";
            db.Boards.Save(board);
            db.Columns.Save(column);
            db.Tasks.Save(kept);
            db.Tasks.Save(gone);
            db.Tasks.Delete(gone.Id);

            List<TaskSearchResult> results = db.Search.SearchTasksWithContext("kanban");
            Assert.Single(results);
            Assert.Equal(kept.Id, results[0].Id);

            db.Boards.DeleteCascade(board.Id);
            Assert.Empty(db.Search.SearchTasksWithContext("kanban"));
        }

        [Fact]
        public void SearchBoards_MatchesSubstring_EscapesWildcards()
        {
            using TestDb db = new TestDb();
            Board plain = Items.Board("Home Projects");
            Board wildcard = Items.Board("100% Done");
            wildcard.Position = "W";
            db.Boards.Save(plain);
            db.Boards.Save(wildcard);

            List<SearchResult> byName = db.Search.SearchBoards("proj");
            Assert.Single(byName);
            Assert.Equal(plain.Id, byName[0].Id);

            List<SearchResult> byWildcard = db.Search.SearchBoards("100%");
            Assert.Single(byWildcard);
            Assert.Equal(wildcard.Id, byWildcard[0].Id);

            Assert.Empty(db.Search.SearchBoards("zzz"));
        }
    }
}
