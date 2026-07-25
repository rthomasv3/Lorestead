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
        public void BacklinkSourcesCarryTitlesSnippetsAndTaskContext()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            db.Notes.Save(target);

            Note fromNote = Items.Note("Source note", $"Context before [Target](note://{target.Id}) context after.");
            db.Notes.Save(fromNote);

            Board board = Items.Board("Roadmap");
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Doing");
            db.Columns.Save(column);
            TaskItem fromTask = Items.Task(column.Id, "Source task", $"Do the thing in [Target](note://{target.Id}).");
            db.Tasks.Save(fromTask);

            List<NoteBacklink> backlinks = db.Notes.GetBacklinkSources(target.Id);
            Assert.Equal(2, backlinks.Count);

            NoteBacklink noteSource = Assert.Single(backlinks, b => b.NoteId != null);
            Assert.Equal("Source note", noteSource.Title);
            Assert.Equal("Context before Target context after.", noteSource.Snippet);

            NoteBacklink taskSource = Assert.Single(backlinks, b => b.TaskId != null);
            Assert.Equal("Source task", taskSource.Title);
            Assert.Equal("Roadmap", taskSource.BoardName);
            Assert.Equal("Doing", taskSource.ColumnName);
            Assert.Equal(board.Id, taskSource.BoardId);
        }

        [Fact]
        public void LinkedNotesBacklinkAndMergeWithBodyMentions()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            db.Notes.Save(target);
            Board board = Items.Board("Roadmap");
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id, "Doing");
            db.Columns.Save(column);

            // Linked only - no note:// anywhere in its body.
            TaskItem linkedOnly = Items.Task(column.Id, "A linked only", "No links in here.",
                new List<string> { target.Id });
            db.Tasks.Save(linkedOnly);

            // Both: linked list AND a body mention. One card, not two.
            TaskItem both = Items.Task(column.Id, "B both", $"See [Target](note://{target.Id}).",
                new List<string> { target.Id });
            db.Tasks.Save(both);

            List<NoteBacklink> backlinks = db.Notes.GetBacklinkSources(target.Id);
            Assert.Equal(2, backlinks.Count);

            NoteBacklink linkOnly = Assert.Single(backlinks, b => b.TaskId == linkedOnly.Id);
            Assert.Equal(BacklinkVia.Link, linkOnly.Via);
            Assert.Equal(string.Empty, linkOnly.Snippet);
            Assert.Equal("Roadmap", linkOnly.BoardName);

            NoteBacklink merged = Assert.Single(backlinks, b => b.TaskId == both.Id);
            Assert.Equal(BacklinkVia.Both, merged.Via);
            Assert.Equal("See Target.", merged.Snippet);
        }

        [Fact]
        public void TrashedNotesAndDeletedTasksAreNotBacklinkSources()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            db.Notes.Save(target);

            Note fromNote = Items.Note("Trashed source", $"[Target](note://{target.Id})");
            db.Notes.Save(fromNote);

            Board board = Items.Board();
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            db.Columns.Save(column);
            TaskItem fromTask = Items.Task(column.Id, "Deleted source", $"[Target](note://{target.Id})");
            db.Tasks.Save(fromTask);

            Assert.Equal(2, db.Notes.GetBacklinkSources(target.Id).Count);

            db.Notes.TrashSubtree(fromNote.Id);
            db.Tasks.Delete(fromTask.Id);

            Assert.Empty(db.Notes.GetBacklinkSources(target.Id));
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
