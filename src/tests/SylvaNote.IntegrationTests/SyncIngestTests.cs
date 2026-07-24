using System;
using System.Collections.Generic;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.DataAccess.Migrations;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    // Server-side upload semantics: the TestDb runs the Shared migration set, so it is
    // the server database.
    public sealed class SyncIngestTests
    {
        private const string DeviceA = "0198c0de-aaaa-7000-8000-00000000dev1";
        private const string DeviceB = "0198c0de-aaaa-7000-8000-00000000dev2";

        [Fact]
        public void IngestStampsSequentialSeqsAndAppliesPayloads()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note first = Items.Note("First", "alpha body");
            Note second = Items.Note("Second", "beta body");

            UploadChangesResponse response = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(first, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
                Upsert(second, DeviceA, null, "2026-07-23T10:00:00.0000002Z"),
            });

            Assert.Equal(2, response.Results.Count);
            Assert.Equal(1, response.Results[0].Seq);
            Assert.Equal(2, response.Results[1].Seq);
            Assert.False(response.Results[0].SupersededConcurrent);
            Assert.Equal("First", db.Notes.Get(first.Id).Title);
            Assert.Equal("Second", db.Notes.Get(second.Id).Title);
            Assert.Equal(2, db.ChangeLog.GetMaxSeq());
            Assert.Single(db.Search.SearchNotes("alpha"));
        }

        [Fact]
        public void SecondBatchContinuesTheSequence()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note note = Items.Note("One");
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
            });

            note.Title = "Two";
            UploadChangesResponse response = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, 1, "2026-07-23T10:00:00.0000002Z"),
            });

            Assert.Equal(2, Assert.Single(response.Results).Seq);
            Assert.Equal("Two", db.Notes.Get(note.Id).Title);
        }

        [Fact]
        public void StaleBaseSeqSetsSupersededConcurrent()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note note = Items.Note("Original");
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
            });

            Note deviceAEdit = Clone(note);
            deviceAEdit.Title = "Device A edit";
            UploadChangesResponse current = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(deviceAEdit, DeviceA, 1, "2026-07-23T10:00:00.0000002Z"),
            });

            // Device B edited from seq 1 without having seen seq 2.
            Note deviceBEdit = Clone(note);
            deviceBEdit.Title = "Device B edit";
            UploadChangesResponse stale = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(deviceBEdit, DeviceB, 1, "2026-07-23T10:00:00.0000003Z"),
            });

            Assert.False(Assert.Single(current.Results).SupersededConcurrent);
            Assert.True(Assert.Single(stale.Results).SupersededConcurrent);
            Assert.Equal("Device B edit", db.Notes.Get(note.Id).Title);
        }

        [Fact]
        public void ConcurrentCreateSetsSupersededConcurrent()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note note = Items.Note("Mine");
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
            });

            Note rival = Clone(note);
            rival.Title = "Also mine";
            UploadChangesResponse response = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(rival, DeviceB, null, "2026-07-23T10:00:00.0000002Z"),
            });

            Assert.True(Assert.Single(response.Results).SupersededConcurrent);
        }

        [Fact]
        public void RetriedUploadReturnsExistingSeqsWithoutDuplicating()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note note = Items.Note("Once");
            List<ChangeLogEntry> batch = new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
            };

            UploadChangesResponse initial = ingestor.Ingest(batch);
            UploadChangesResponse retried = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
            });

            Assert.Equal(Assert.Single(initial.Results).Seq, Assert.Single(retried.Results).Seq);
            Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, note.Id));
            Assert.Equal(1, db.ChangeLog.GetMaxSeq());
        }

        [Fact]
        public void PurgeRemovesItemAndHistoryButKeepsPurgeEntry()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note note = Items.Note("Doomed", "purge target");
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(note, DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
            });

            UploadChangesResponse response = ingestor.Ingest(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    ItemType = ItemTypes.Note,
                    ItemId = note.Id,
                    Op = ChangeOps.Purge,
                    Payload = "",
                    BaseSeq = 1,
                    DeviceId = DeviceA,
                    ChangedAt = "2026-07-23T10:00:00.0000002Z",
                },
            });

            Assert.Equal(2, Assert.Single(response.Results).Seq);
            Assert.Null(db.Notes.Get(note.Id));
            Assert.Empty(db.Search.SearchNotes("purge"));
            ChangeLogEntry remaining = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, note.Id));
            Assert.Equal(ChangeOps.Purge, remaining.Op);
            Assert.Equal(2, remaining.Seq);
        }

        [Fact]
        public void IngestPrunesItemHistoryBeyondRetention()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager, historyRetention: 3);
            Note note = Items.Note("v1");

            for (int version = 1; version <= 5; version++)
            {
                note.Title = $"v{version}";
                ingestor.Ingest(new List<ChangeLogEntry>
                {
                    Upsert(Clone(note), DeviceA, version == 1 ? (long?)null : version - 1, $"2026-07-23T10:00:00.000000{version}Z"),
                });
            }

            List<ChangeLogEntry> history = db.ChangeLog.GetForItem(ItemTypes.Note, note.Id);
            Assert.Equal(3, history.Count);
            Assert.Equal(5, history[0].Seq);
            Assert.Equal(3, history[2].Seq);
            Assert.Equal("v5", db.Notes.Get(note.Id).Title);
        }

        [Fact]
        public void ExpiredPurgeEntriesPruneAndRaiseTheWatermark()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note purged = Items.Note("Long gone");
            Note survivor = Items.Note("Still here");

            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(purged, DeviceA, null, "2020-01-01T00:00:00.0000001Z"),
                Upsert(survivor, DeviceA, null, "2020-01-01T00:00:00.0000002Z"),
                new ChangeLogEntry
                {
                    ItemType = ItemTypes.Note,
                    ItemId = purged.Id,
                    Op = ChangeOps.Purge,
                    Payload = "",
                    BaseSeq = 1,
                    DeviceId = DeviceA,
                    ChangedAt = "2020-01-01T00:00:00.0000003Z",
                },
            });

            new ChangeLogPruner(db.ConnectionManager).PruneExpiredPurgeEntries(90);

            // The ancient purge entry ages out and moves the watermark to its seq; the
            // equally ancient upsert stays - it is still the survivor's live version.
            Assert.Empty(db.ChangeLog.GetForItem(ItemTypes.Note, purged.Id));
            Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, survivor.Id));
            Assert.Equal(3, new ServerStateRepository(db.ConnectionManager).GetPrunedThroughSeq());
        }

        [Fact]
        public void SeqsAreNeverReusedAfterTailDeletion()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Note doomed = Items.Note("Doomed");
            Note keeper = Items.Note("Keeper");
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(doomed, DeviceA, null, "2020-01-01T00:00:00.0000001Z"),
                Upsert(keeper, DeviceA, null, "2020-01-01T00:00:00.0000002Z"),
                new ChangeLogEntry
                {
                    ItemType = ItemTypes.Note,
                    ItemId = doomed.Id,
                    Op = ChangeOps.Purge,
                    Payload = "",
                    BaseSeq = 1,
                    DeviceId = DeviceA,
                    ChangedAt = "2020-01-01T00:00:00.0000003Z",
                },
            });

            // Purge cascade deleted seq 1 and pruning deletes seq 3 - MAX(seq) is now
            // 2, but the next allocation must still be 4 or a cursor at 3 skips it.
            new ChangeLogPruner(db.ConnectionManager).PruneExpiredPurgeEntries(0);

            keeper.Title = "Edited";
            UploadChangesResponse response = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(keeper, DeviceA, 2, "2020-01-01T00:00:00.0000004Z"),
            });

            Assert.Equal(4, Assert.Single(response.Results).Seq);
        }

        [Fact]
        public void GetAfterPagesInSeqOrder()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                Upsert(Items.Note("A"), DeviceA, null, "2026-07-23T10:00:00.0000001Z"),
                Upsert(Items.Note("B"), DeviceA, null, "2026-07-23T10:00:00.0000002Z"),
                Upsert(Items.Note("C"), DeviceA, null, "2026-07-23T10:00:00.0000003Z"),
            });

            List<ChangeLogEntry> firstPage = db.ChangeLog.GetAfter(0, 2);
            List<ChangeLogEntry> secondPage = db.ChangeLog.GetAfter(2, 10);

            Assert.Equal(new long?[] { 1, 2 }, new long?[] { firstPage[0].Seq, firstPage[1].Seq });
            Assert.Equal(3, Assert.Single(secondPage).Seq);
        }

        [Fact]
        public void UnknownItemTypeIsRejected()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);

            Assert.Throws<ArgumentException>(() => ingestor.Ingest(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    ItemType = "mystery",
                    ItemId = Items.NewId(),
                    Op = ChangeOps.Upsert,
                    Payload = "{}",
                    DeviceId = DeviceA,
                    ChangedAt = "2026-07-23T10:00:00.0000001Z",
                },
            }));
        }

        private static ChangeLogEntry Upsert(Note note, string deviceId, long? baseSeq, string changedAt)
        {
            note.CreatedAt = note.CreatedAt ?? changedAt;
            note.UpdatedAt = changedAt;
            return new ChangeLogEntry
            {
                ItemType = ItemTypes.Note,
                ItemId = note.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(note),
                BaseSeq = baseSeq,
                DeviceId = deviceId,
                ChangedAt = changedAt,
            };
        }

        private static Note Clone(Note note)
        {
            return PayloadJson.Deserialize<Note>(PayloadJson.Serialize(note));
        }
    }
}
