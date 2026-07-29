using System.Collections.Generic;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    public sealed class OutboxTests
    {
        [Fact]
        public void SaveAppendsPendingEntryInSameState()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Hello", "World");
            db.Notes.Save(note);

            List<PendingChange> pending = db.ChangeLog.GetPending();
            PendingChange change = Assert.Single(pending);
            Assert.Null(change.Entry.Seq);
            Assert.Equal(ItemTypes.Note, change.Entry.ItemType);
            Assert.Equal(note.Id, change.Entry.ItemId);
            Assert.Equal(ChangeOps.Upsert, change.Entry.Op);
            Assert.Equal(db.DeviceId, change.Entry.DeviceId);
            Assert.Null(change.Entry.BaseSeq);

            Note payload = PayloadJson.Deserialize<Note>(change.Entry.Payload);
            Assert.Equal("Hello", payload.Title);
            Assert.Equal("World", payload.Body);
            Assert.Equal(note.UpdatedAt, payload.UpdatedAt);
        }

        [Fact]
        public void PendingEntriesKeepEditOrderAcrossTypes()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note();
            Board board = Items.Board();
            db.Notes.Save(note);
            db.Boards.Save(board);
            db.Notes.Save(note);

            List<PendingChange> pending = db.ChangeLog.GetPending();
            Assert.Equal(3, pending.Count);
            Assert.Equal(note.Id, pending[0].Entry.ItemId);
            Assert.Equal(board.Id, pending[1].Entry.ItemId);
            Assert.Equal(note.Id, pending[2].Entry.ItemId);
        }

        [Fact]
        public void AssignSeqDrainsOutbox()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note();
            db.Notes.Save(note);
            db.Notes.Save(note);

            List<PendingChange> pending = db.ChangeLog.GetPending();
            Assert.Equal(2, pending.Count);

            db.ChangeLog.AssignSeq(pending[0].LocalId, 1);
            Assert.Single(db.ChangeLog.GetPending());

            db.ChangeLog.AssignSeq(pending[1].LocalId, 2);
            Assert.Empty(db.ChangeLog.GetPending());
            Assert.Equal(2, db.ChangeLog.GetMaxSeq());
        }

        [Fact]
        public void BaseSeqTracksLatestSyncedVersion()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note();
            db.Notes.Save(note);

            List<PendingChange> first = db.ChangeLog.GetPending();
            db.ChangeLog.AssignSeq(first[0].LocalId, 7);

            db.Notes.Save(note);
            List<PendingChange> second = db.ChangeLog.GetPending();
            PendingChange change = Assert.Single(second);
            Assert.Equal(7, change.Entry.BaseSeq);
        }

        [Fact]
        public void HistoryListsAllVersionsNewestFirst()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("v1");
            db.Notes.Save(note);
            note.Title = "v2";
            db.Notes.Save(note);

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, note.Id);
            Assert.Equal(2, history.Count);
            Assert.Equal("v2", PayloadJson.Deserialize<Note>(history[0].Payload).Title);
            Assert.Equal("v1", PayloadJson.Deserialize<Note>(history[1].Payload).Title);
        }

        [Fact]
        public void SaveIsAtomicItemPlusOutbox()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note();
            db.Notes.Save(note);

            Assert.NotNull(db.Notes.Get(note.Id));
            Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, note.Id));
        }
    }
}
