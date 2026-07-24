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
        public void PendingLocalEditIsNotClobberedByForeignPull()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note note = Items.Note("A-edit");
            db.Notes.Save(note);

            Note foreign = ClonePayload(db.Notes.Get(note.Id));
            foreign.Title = "B-edit";
            applier.Apply(new List<ChangeLogEntry> { ForeignUpsert(foreign, 10) });

            // The pending outbox edit outranks the pulled entry once uploaded - the row
            // must keep showing it, while the entry still mirrors and moves the cursor.
            Assert.Equal("A-edit", db.Notes.Get(note.Id).Title);
            Assert.Single(db.ChangeLog.GetPending());
            Assert.Contains(db.ChangeLog.GetForItem(ItemTypes.Note, note.Id), e => e.Seq == 10);
            Assert.Equal(10, db.SyncState.Get().LastSeenSeq);
        }

        [Fact]
        public void StampedNewerSeqBlocksOlderForeignEntry()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note note = Items.Note("A-edit");
            db.Notes.Save(note);
            // Outbox drain raced the pull: the upload response already stamped seq 11.
            PendingChange pending = Assert.Single(db.ChangeLog.GetPending());
            db.ChangeLog.AssignSeq(pending.LocalId, 11);

            Note foreign = ClonePayload(db.Notes.Get(note.Id));
            foreign.Title = "B-edit";
            applier.Apply(new List<ChangeLogEntry> { ForeignUpsert(foreign, 10) });

            Assert.Equal("A-edit", db.Notes.Get(note.Id).Title);
            Assert.Contains(db.ChangeLog.GetForItem(ItemTypes.Note, note.Id), e => e.Seq == 10);
        }

        [Fact]
        public void PurgeIsSkippedWhileALocalEditIsPending()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Note note = Items.Note("Survivor");
            db.Notes.Save(note);

            applier.Apply(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Seq = 10,
                    ItemType = ItemTypes.Note,
                    ItemId = note.Id,
                    Op = ChangeOps.Purge,
                    Payload = "",
                    DeviceId = OtherDevice,
                    ChangedAt = Timestamps.UtcNowIso(),
                },
            });

            // The pending edit will resurrect the item on the server (ingest treats a
            // post-purge edit as a recreate), so deleting locally would diverge.
            Assert.NotNull(db.Notes.Get(note.Id));
            Assert.Single(db.ChangeLog.GetPending());
        }

        [Fact]
        public void OwnEntryWithoutPendingMatchAppliesLikeForeign()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            // Post-resync wipe: this device's own stamped entries come back through the
            // full pull and must rebuild the item row.
            Note note = Items.Note("Mine, from the server");
            Stamp(note);
            applier.Apply(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Seq = 1,
                    ItemType = ItemTypes.Note,
                    ItemId = note.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(note),
                    DeviceId = db.DeviceId,
                    ChangedAt = Timestamps.UtcNowIso(),
                },
            });

            Assert.Equal("Mine, from the server", db.Notes.Get(note.Id).Title);
        }

        [Fact]
        public void PullPrunesMirroredHistoryButNeverPendingEntries()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId, historyRetention: 2);

            Note mirrored = Items.Note("Remote");
            Stamp(mirrored);
            for (long seq = 1; seq <= 4; seq++)
            {
                mirrored.Title = $"Remote v{seq}";
                applier.Apply(new List<ChangeLogEntry> { ForeignUpsert(ClonePayload(mirrored), seq) });
            }

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, mirrored.Id);
            Assert.Equal(2, history.Count);
            Assert.Equal(4, history[0].Seq);

            // An item with a pending local edit only mirrors pulled entries, and the
            // pending entry itself is never prune-eligible.
            Note local = Items.Note("Local");
            db.Notes.Save(local);
            Note foreignLocal = ClonePayload(db.Notes.Get(local.Id));
            for (long seq = 5; seq <= 8; seq++)
            {
                foreignLocal.Title = $"Foreign v{seq}";
                applier.Apply(new List<ChangeLogEntry> { ForeignUpsert(ClonePayload(foreignLocal), seq) });
            }

            Assert.Equal("Local", db.Notes.Get(local.Id).Title);
            Assert.Single(db.ChangeLog.GetPending());
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
