using System;
using System.Collections.Generic;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class ChangeApplierTests
    {
        private const string OtherDevice = "0198c0de-aaaa-7000-8000-00000000dev2";

        [Fact]
        public void ForeignUpsertAppliesMirrorsAndAdvancesCursor()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note incoming = Items.Note("From elsewhere", "synced body");
            Stamp(incoming);
            applier.Apply(new List<ChangeLogEntry> { ForeignUpsert(incoming, 1) });

            Note applied = db.Notes.Get(incoming.Id);
            Assert.NotNull(applied);
            Assert.Equal("From elsewhere", applied.Title);

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, incoming.Id);
            ChangeLogEntry mirrored = Assert.Single(history);
            Assert.Equal(1, mirrored.Seq);
            Assert.Equal(1, db.SyncState.Get().LastSeenSeq);
        }

        [Fact]
        public void LastWriterWinsAcrossEntries()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note version1 = Items.Note("first");
            Stamp(version1);
            Note version2 = ClonePayload(version1);
            version2.Title = "second";

            applier.Apply(new List<ChangeLogEntry>
            {
                ForeignUpsert(version2, 2),
                ForeignUpsert(version1, 1),
            });

            Assert.Equal("second", db.Notes.Get(version1.Id).Title);
            Assert.Equal(2, db.ChangeLog.GetForItem(ItemTypes.Note, version1.Id).Count);
            Assert.Equal(2, db.SyncState.Get().LastSeenSeq);
        }

        [Fact]
        public void ReapplyingABatchIsIdempotent()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note incoming = Items.Note("once");
            Stamp(incoming);
            List<ChangeLogEntry> batch = new List<ChangeLogEntry> { ForeignUpsert(incoming, 1) };
            applier.Apply(batch);
            applier.Apply(batch);

            Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, incoming.Id));
        }

        [Fact]
        public void OwnEntriesStampPendingAndNeverReapply()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note note = Items.Note("local v1");
            db.Notes.Save(note);
            List<PendingChange> pending = db.ChangeLog.GetPending();
            ChangeLogEntry uploaded = pending[0].Entry;

            note.Title = "local v2";
            db.Notes.Save(note);

            ChangeLogEntry echoed = new ChangeLogEntry
            {
                Seq = 1,
                ItemType = uploaded.ItemType,
                ItemId = uploaded.ItemId,
                Op = uploaded.Op,
                Payload = uploaded.Payload,
                BaseSeq = uploaded.BaseSeq,
                DeviceId = db.DeviceId,
                ChangedAt = uploaded.ChangedAt,
            };
            applier.Apply(new List<ChangeLogEntry> { echoed });

            // The older own change must not clobber the newer local edit.
            Assert.Equal("local v2", db.Notes.Get(note.Id).Title);

            List<PendingChange> stillPending = db.ChangeLog.GetPending();
            PendingChange remaining = Assert.Single(stillPending);
            Assert.Equal("local v2", PayloadJson.Deserialize<Note>(remaining.Entry.Payload).Title);
        }

        [Fact]
        public void PurgeRemovesItemHistoryAndCascades()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note note = Items.Note("doomed", "purge me");
            Stamp(note);
            applier.Apply(new List<ChangeLogEntry> { ForeignUpsert(note, 1) });

            Attachment attachment = Items.Attachment(noteId: note.Id);
            Stamp(attachment);
            applier.Apply(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Seq = 2,
                    ItemType = ItemTypes.Attachment,
                    ItemId = attachment.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(attachment),
                    DeviceId = OtherDevice,
                    ChangedAt = Timestamps.UtcNowIso(),
                },
            });

            applier.Apply(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Seq = 3,
                    ItemType = ItemTypes.Note,
                    ItemId = note.Id,
                    Op = ChangeOps.Purge,
                    Payload = "",
                    DeviceId = OtherDevice,
                    ChangedAt = Timestamps.UtcNowIso(),
                },
            });

            Assert.Null(db.Notes.Get(note.Id));
            Assert.Null(db.Attachments.Get(attachment.Id));
            Assert.Empty(db.Search.SearchNotes("purge"));

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, note.Id);
            ChangeLogEntry purgeEntry = Assert.Single(history);
            Assert.Equal(ChangeOps.Purge, purgeEntry.Op);
            Assert.Equal(3, db.SyncState.Get().LastSeenSeq);
        }

        [Fact]
        public void TaskUpsertRebuildsDerivedState()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note linked = Items.Note("linked");
            db.Notes.Save(linked);
            Board board = Items.Board();
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            db.Columns.Save(column);

            TaskItem task = Items.Task(column.Id, "remote task", $"body note://{linked.Id}",
                new List<string> { linked.Id });
            Stamp(task);
            applier.Apply(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Seq = 1,
                    ItemType = ItemTypes.Task,
                    ItemId = task.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(task),
                    DeviceId = OtherDevice,
                    ChangedAt = Timestamps.UtcNowIso(),
                },
            });

            TaskItem applied = db.Tasks.Get(task.Id);
            Assert.Equal(linked.Id, Assert.Single(applied.NoteIds));

            List<NoteLink> backlinks = db.Notes.GetBacklinks(linked.Id);
            Assert.Single(backlinks);
            Assert.Single(db.Search.SearchTasks("remote"));
        }

        [Fact]
        public void UnknownItemTypeThrows()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Assert.Throws<InvalidOperationException>(() => applier.Apply(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Seq = 1,
                    ItemType = "mystery",
                    ItemId = Items.NewId(),
                    Op = ChangeOps.Upsert,
                    Payload = "{}",
                    DeviceId = OtherDevice,
                    ChangedAt = Timestamps.UtcNowIso(),
                },
            }));
        }

        private static void Stamp(Note note)
        {
            note.CreatedAt = Timestamps.UtcNowIso();
            note.UpdatedAt = note.CreatedAt;
        }

        private static void Stamp(TaskItem task)
        {
            task.CreatedAt = Timestamps.UtcNowIso();
            task.UpdatedAt = task.CreatedAt;
        }

        private static void Stamp(Attachment attachment)
        {
            attachment.CreatedAt = Timestamps.UtcNowIso();
            attachment.UpdatedAt = attachment.CreatedAt;
        }

        private static Note ClonePayload(Note note)
        {
            return PayloadJson.Deserialize<Note>(PayloadJson.Serialize(note));
        }

        private static ChangeLogEntry ForeignUpsert(Note note, long seq)
        {
            return new ChangeLogEntry
            {
                Seq = seq,
                ItemType = ItemTypes.Note,
                ItemId = note.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(note),
                DeviceId = OtherDevice,
                ChangedAt = Timestamps.UtcNowIso(),
            };
        }
    }
}
