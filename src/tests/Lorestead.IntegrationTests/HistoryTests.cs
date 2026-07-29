using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    // The history feature's read side: what the version list is built from.
    public sealed class HistoryTests
    {
        [Fact]
        public void VersionsAreNewestFirstAndCarryTheirPayload()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("v1", "body 1");
            db.Notes.Save(note);
            note.Title = "v2";
            note.Body = "body 2";
            db.Notes.Save(note);
            note.Title = "v3";
            note.Body = "body 3";
            db.Notes.Save(note);

            List<ItemVersion> versions = db.ChangeLog.GetVersionsForItem(ItemTypes.Note, note.Id);

            Assert.Equal(3, versions.Count);
            Assert.Contains("body 3", versions[0].Payload);
            Assert.Contains("body 1", versions[2].Payload);
            // The local row id is the list's stable key - ChangeLogEntry does not
            // carry it, which is why this read has its own projection.
            Assert.All(versions, version => Assert.True(version.Id > 0));
        }

        [Fact]
        public void PurgeEntriesAreNotVersions()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Note", "still here");
            db.Notes.Save(note);
            AppendPurge(db, note.Id);

            ItemVersion only = Assert.Single(db.ChangeLog.GetVersionsForItem(ItemTypes.Note, note.Id));
            Assert.Contains("still here", only.Payload);
        }

        [Fact]
        public void TheListNeverExceedsTheRetentionCap()
        {
            using TestDb db = new TestDb(historyRetention: 2);
            Note note = Items.Note("v1", "body 1");
            for (int version = 1; version <= 5; version++)
            {
                note.Title = $"v{version}";
                note.Body = $"body {version}";
                db.Notes.Save(note);
            }

            List<ItemVersion> versions = db.ChangeLog.GetVersionsForItem(ItemTypes.Note, note.Id);
            Assert.Equal(2, versions.Count);
            Assert.Contains("body 5", versions[0].Payload);
        }

        [Fact]
        public void AVersionCanBeFetchedByIdButOnlyForItsOwnNote()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Mine", "my body");
            db.Notes.Save(note);
            Note other = Items.Note("Theirs", "their body");
            db.Notes.Save(other);

            ItemVersion mine = Assert.Single(db.ChangeLog.GetVersionsForItem(ItemTypes.Note, note.Id));

            ItemVersion found = db.ChangeLog.GetVersionForItem(ItemTypes.Note, note.Id, mine.Id);
            Assert.NotNull(found);
            Assert.Contains("my body", found.Payload);

            // Restore names a version by local row id, so that id has to be proven to
            // belong to the note being restored.
            Assert.Null(db.ChangeLog.GetVersionForItem(ItemTypes.Note, other.Id, mine.Id));
            Assert.Null(db.ChangeLog.GetVersionForItem(ItemTypes.Note, note.Id, mine.Id + 9999));
        }

        private static void AppendPurge(TestDb db, string noteId)
        {
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Note,
                ItemId = noteId,
                Op = ChangeOps.Purge,
                DeviceId = db.DeviceId,
                ChangedAt = Timestamps.UtcNowIso(),
            });
            transaction.Commit();
        }
    }
}
