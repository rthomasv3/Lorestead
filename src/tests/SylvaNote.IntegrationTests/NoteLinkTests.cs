using System.Collections.Generic;
using SylvaNote.Core.Entities;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class NoteLinkTests
    {
        [Fact]
        public void SavingANoteIndexesItsLinks()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            db.Notes.Save(target);
            Note source = Items.Note("Source", $"See [target](note://{target.Id}).");
            db.Notes.Save(source);

            List<NoteLink> backlinks = db.Notes.GetBacklinks(target.Id);
            NoteLink link = Assert.Single(backlinks);
            Assert.Equal(source.Id, link.FromNoteId);
            Assert.Null(link.FromTaskId);
        }

        [Fact]
        public void RemovingTheLinkClearsTheIndexOnResave()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            db.Notes.Save(target);
            Note source = Items.Note("Source", $"See [target](note://{target.Id}).");
            db.Notes.Save(source);

            source.Body = "No more links.";
            db.Notes.Save(source);

            Assert.Empty(db.Notes.GetBacklinks(target.Id));
        }

        [Fact]
        public void MissingTargetsAreSkippedNotIndexed()
        {
            using TestDb db = new TestDb();
            Note source = Items.Note("Source", "Broken [link](note://0198c0de-dead-7000-8000-000000000000).");
            db.Notes.Save(source);

            Assert.Empty(db.Notes.GetBacklinks("0198c0de-dead-7000-8000-000000000000"));
        }

        [Fact]
        public void TaskBodiesBacklinkToo()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            db.Notes.Save(target);
            Board board = Items.Board();
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            db.Columns.Save(column);
            TaskItem task = Items.Task(column.Id, "Linker", $"references note://{target.Id}");
            db.Tasks.Save(task);

            List<NoteLink> backlinks = db.Notes.GetBacklinks(target.Id);
            NoteLink link = Assert.Single(backlinks);
            Assert.Equal(task.Id, link.FromTaskId);
            Assert.Null(link.FromNoteId);
        }

        [Fact]
        public void TaskNoteLinksArePersistedAndSkipMissingTargets()
        {
            using TestDb db = new TestDb();
            Note linked = Items.Note("Linked");
            db.Notes.Save(linked);
            Board board = Items.Board();
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            db.Columns.Save(column);

            TaskItem task = Items.Task(column.Id, noteIds: new List<string> { linked.Id, "0198c0de-dead-7000-8000-000000000000" });
            db.Tasks.Save(task);

            TaskItem loaded = db.Tasks.Get(task.Id);
            string noteId = Assert.Single(loaded.NoteIds);
            Assert.Equal(linked.Id, noteId);
        }
    }
}
