using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    // The per-item history cap, from the angles that the seq-ordered version got
    // wrong: a serverless install where nothing is ever stamped, and a synced one
    // where arrival order and authored order disagree.
    public sealed class RetentionTests
    {
        [Fact]
        public void ServerlessSavesAreCappedWithoutASingleStampedEntry()
        {
            using TestDb db = new TestDb(historyRetention: 3);
            Note note = Items.Note("v1");

            for (int version = 1; version <= 8; version++)
            {
                note.Title = $"v{version}";
                note.Body = $"body {version}";
                db.Notes.Save(note);
            }

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, note.Id);
            Assert.Equal(3, history.Count);
            Assert.All(history, entry => Assert.Null(entry.Seq));
            Assert.Contains("body 8", history[0].Payload);
        }

        [Fact]
        public void TheNewestPendingEntrySurvivesEvenWhenItSortsOldest()
        {
            // A local edit made before a burst of newer remote versions arrives:
            // beyond the cap by authored time, but still owed to the outbox.
            using TestDb db = new TestDb();
            string noteId = Items.NewId();
            Append(db, noteId, null, "2026-01-01T00:00:00.0000000Z");
            Append(db, noteId, 1, "2026-06-01T00:00:00.0000000Z");
            Append(db, noteId, 2, "2026-06-02T00:00:00.0000000Z");
            Append(db, noteId, 3, "2026-06-03T00:00:00.0000000Z");

            Prune(db, noteId, keep: 2);

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, noteId);
            Assert.Equal(3, history.Count);
            Assert.Single(history, entry => entry.Seq == null);
            Assert.DoesNotContain(history, entry => entry.Seq == 1);
        }

        [Fact]
        public void OnlyTheNewestPendingEntryIsProtected()
        {
            // Older pending entries are redundant - payloads are full, not deltas.
            using TestDb db = new TestDb();
            string noteId = Items.NewId();
            Append(db, noteId, null, "2026-01-01T00:00:00.0000000Z");
            Append(db, noteId, null, "2026-01-02T00:00:00.0000000Z");
            Append(db, noteId, null, "2026-01-03T00:00:00.0000000Z");

            Prune(db, noteId, keep: 1);

            ChangeLogEntry remaining = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, noteId));
            Assert.Equal("2026-01-03T00:00:00.0000000Z", remaining.ChangedAt);
        }

        [Fact]
        public void ArrivalOrderDoesNotEvictTheNewestAuthoredVersion()
        {
            // A pull inserts older remote entries last, giving them the highest local
            // ids - ordering by id would keep seq 9 and drop the newest version.
            using TestDb db = new TestDb();
            string noteId = Items.NewId();
            Append(db, noteId, 10, "2026-06-10T00:00:00.0000000Z");
            Append(db, noteId, 8, "2026-06-08T00:00:00.0000000Z");
            Append(db, noteId, 9, "2026-06-09T00:00:00.0000000Z");

            Prune(db, noteId, keep: 1);

            ChangeLogEntry remaining = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, noteId));
            Assert.Equal(10, remaining.Seq);
        }

        private static void Append(TestDb db, string noteId, long? seq, string changedAt)
        {
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                Seq = seq,
                ItemType = ItemTypes.Note,
                ItemId = noteId,
                Op = ChangeOps.Upsert,
                Payload = changedAt,
                DeviceId = db.DeviceId,
                ChangedAt = changedAt,
            });
            transaction.Commit();
        }

        private static void Prune(TestDb db, string noteId, int keep)
        {
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            ChangeLogRepository.PruneItemVersionsWithin(connection, transaction, ItemTypes.Note, noteId, keep);
            transaction.Commit();
        }
    }
}
