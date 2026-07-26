using System.Collections.Generic;
using SylvaNote.Core.Entities;
using SylvaNote.Core.FirstRun;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class FirstRunSeedTests
    {
        [Fact]
        public void SeedsTheTourTheBoardAndTheTemplate()
        {
            using TestDb db = new TestDb();
            FirstRunSeeder.Seed(db.ConnectionManager, db.DeviceId);

            List<Note> notes = db.Notes.GetAll();
            Assert.Equal(11, notes.Count);
            Assert.DoesNotContain(notes, note => note.Deleted);

            Note root = Find(notes, SeedIds.GettingStartedNote);
            Assert.Null(root.ParentId);
            Assert.Equal(NoteType.Normal, root.Type);
            Assert.Equal(6, notes.FindAll(note => note.ParentId == SeedIds.GettingStartedNote).Count);

            Note template = Find(notes, SeedIds.ProjectTemplate);
            Assert.Null(template.ParentId);
            Assert.Equal(NoteType.Template, template.Type);
            Assert.Equal(3, notes.FindAll(note => note.ParentId == SeedIds.ProjectTemplate).Count);

            Board board = Assert.Single(db.Boards.GetActive());
            Assert.Equal(SeedIds.LearnBoard, board.Id);
            Assert.Equal(2, db.Columns.GetActiveForBoard(board.Id).Count);
            Assert.Equal(6, db.Tasks.GetActiveForBoard(board.Id).Count);
            Assert.Empty(db.Tasks.GetForColumn(SeedIds.DoneColumn));
        }

        [Fact]
        public void TheIconArrivesAsAnAttachmentWithItsBlob()
        {
            using TestDb db = new TestDb();
            FirstRunSeeder.Seed(db.ConnectionManager, db.DeviceId);

            Attachment attachment = Assert.Single(db.Attachments.GetForNote(SeedIds.GettingStartedNote));
            Assert.Equal(SeedIds.IconAttachment, attachment.Id);
            Assert.Equal("image/png", attachment.MimeType);

            byte[] blob = db.Attachments.GetBlob(attachment.Id);
            Assert.Equal(attachment.SizeBytes, blob.Length);
            Assert.Equal(0x89, blob[0]);
            Assert.Equal((byte)'P', blob[1]);

            // Core has no imaging dependency; the frontend renders one on first view.
            Assert.Null(db.Attachments.GetThumbnail(attachment.Id));
        }

        // The tour notes point at each other, so the index has to be built after every
        // row exists - a link to a target that is not there yet is silently dropped.
        [Fact]
        public void SeedLinksAreIndexedInBothDirections()
        {
            using TestDb db = new TestDb();
            FirstRunSeeder.Seed(db.ConnectionManager, db.DeviceId);

            List<NoteBacklink> backlinks = db.Notes.GetBacklinkSources(SeedIds.EditorNote);
            Assert.Contains(backlinks, source => source.NoteId == SeedIds.GettingStartedNote);
            Assert.Contains(backlinks, source => source.NoteId == SeedIds.SearchNote);

            // A card links its note from the body and carries it in the linked-notes
            // list, which is one source showing both halves rather than two cards.
            NoteBacklink card = Assert.Single(backlinks.FindAll(source => source.TaskId == SeedIds.FirstNoteTask));
            Assert.Equal(BacklinkVia.Both, card.Via);
            Assert.Equal("Learn SylvaNote", card.BoardName);
        }

        // Version 0 of every item, stamped in the past, so a later edit or deletion on
        // any device wins LWW against a fresh device's pristine seed (decisions.md).
        [Fact]
        public void EverythingIsStampedInThePastAndPendingUpload()
        {
            using TestDb db = new TestDb();
            FirstRunSeeder.Seed(db.ConnectionManager, db.DeviceId);

            Note root = db.Notes.Get(SeedIds.GettingStartedNote);
            Assert.Equal(root.CreatedAt, root.UpdatedAt);

            // 11 notes, 1 attachment, 1 board, 2 columns, 6 tasks - each its own entry,
            // waiting in the outbox exactly as a hand-typed note would be.
            List<PendingChange> pending = db.ChangeLog.GetPending();
            Assert.Equal(21, pending.Count);
            Assert.All(pending, change => Assert.Equal(root.UpdatedAt, change.Entry.ChangedAt));
            Assert.All(pending, change => Assert.Null(change.Entry.BaseSeq));

            Note edited = Items.Note("Written today");
            db.Notes.Save(edited);
            Assert.True(string.CompareOrdinal(root.UpdatedAt, edited.UpdatedAt) < 0);
        }

        // Two hosts can open a nonexistent database at once and both conclude they
        // created it. Fixed ids are what keeps that from doubling the content.
        [Fact]
        public void SeedingTwiceLeavesOneCopy()
        {
            using TestDb db = new TestDb();
            FirstRunSeeder.Seed(db.ConnectionManager, db.DeviceId);
            FirstRunSeeder.Seed(db.ConnectionManager, db.DeviceId);

            Assert.Equal(11, db.Notes.GetAll().Count);
            Assert.Single(db.Boards.GetActive());
            Assert.Equal(6, db.Tasks.GetActiveForBoard(SeedIds.LearnBoard).Count);
            Assert.Single(db.Attachments.GetForNote(SeedIds.GettingStartedNote));
        }

        private static Note Find(List<Note> notes, string id)
        {
            return notes.Find(note => note.Id == id);
        }
    }
}
