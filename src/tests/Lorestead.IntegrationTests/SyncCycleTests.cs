using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    // End-to-end sync: two client DBs (full client schema) converging through the
    // real server pipeline booted by ServerFixture.
    public sealed class SyncCycleTests
    {
        [Fact]
        public async Task TwoDevicesConvergeThroughTheServer()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using TestDb deviceB = new TestDb();
            using HttpClient httpA = new HttpClient();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);

            Note note = Items.Note("From A", "written on device A");
            deviceA.Notes.Save(note);
            SyncCycleResult uploadResult = await cycleA.Run();
            SyncCycleResult downloadResult = await cycleB.Run();

            Assert.Equal(1, uploadResult.Uploaded);
            Assert.Contains(ItemTypes.Note, downloadResult.ChangedItemTypes);
            Assert.Equal("From A", deviceB.Notes.Get(note.Id).Title);

            Note edited = deviceB.Notes.Get(note.Id);
            edited.Title = "Edited on B";
            deviceB.Notes.Save(edited);
            await cycleB.Run();
            await cycleA.Run();

            Assert.Equal("Edited on B", deviceA.Notes.Get(note.Id).Title);
            Assert.Equal(deviceA.SyncState.Get().LastSeenSeq, deviceB.SyncState.Get().LastSeenSeq);
            Assert.Empty(deviceA.ChangeLog.GetPending());
            Assert.Empty(deviceB.ChangeLog.GetPending());
        }

        [Fact]
        public async Task ConcurrentEditsResolveToTheLastUploadEverywhere()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using TestDb deviceB = new TestDb();
            using HttpClient httpA = new HttpClient();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);

            Note note = Items.Note("Base");
            deviceA.Notes.Save(note);
            await cycleA.Run();
            await cycleB.Run();

            Note editA = deviceA.Notes.Get(note.Id);
            editA.Title = "A's version";
            deviceA.Notes.Save(editA);
            Note editB = deviceB.Notes.Get(note.Id);
            editB.Title = "B's version";
            deviceB.Notes.Save(editB);

            // B reaches the server first; A uploads second and wins the LWW race. A's
            // pull inside the same cycle mirrors B's entry without clobbering the row.
            await cycleB.Run();
            await cycleA.Run();
            await cycleB.Run();

            Assert.Equal("A's version", deviceA.Notes.Get(note.Id).Title);
            Assert.Equal("A's version", deviceB.Notes.Get(note.Id).Title);
        }

        [Fact]
        public async Task StaleDeviceResyncsAndKeepsItsPendingEdits()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using TestDb deviceB = new TestDb();
            using HttpClient httpA = new HttpClient();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);

            Note doomed = Items.Note("Doomed");
            Note keeper = Items.Note("Keeper");
            deviceA.Notes.Save(doomed);
            deviceA.Notes.Save(keeper);
            await cycleA.Run();
            await cycleB.Run();

            // B edits while offline; meanwhile A purges the other note and the purge
            // entry ages out of the server log, stranding B's cursor.
            Note offlineEdit = deviceB.Notes.Get(keeper.Id);
            offlineEdit.Title = "Keeper edited on B";
            deviceB.Notes.Save(offlineEdit);

            deviceA.Notes.PurgeSubtree(doomed.Id);
            await cycleA.Run();
            new ChangeLogPruner(server.ConnectionManager).PruneExpiredPurgeEntries(0);

            SyncCycleResult resync = await cycleB.Run();

            Assert.True(resync.Resynced);
            Assert.Null(deviceB.Notes.Get(doomed.Id));
            Assert.Equal("Keeper edited on B", deviceB.Notes.Get(keeper.Id).Title);
            Assert.Empty(deviceB.ChangeLog.GetPending());

            await cycleA.Run();
            Assert.Equal("Keeper edited on B", deviceA.Notes.Get(keeper.Id).Title);
        }

        [Fact]
        public async Task BlobsUploadAfterMetadataAndBackfillOnPull()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using TestDb deviceB = new TestDb();
            using HttpClient httpA = new HttpClient();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);

            Note owner = Items.Note("Has attachment");
            deviceA.Notes.Save(owner);
            Attachment attachment = Items.Attachment(noteId: owner.Id);
            deviceA.Attachments.Save(attachment);
            byte[] blob = Encoding.UTF8.GetBytes("attachment bytes");
            deviceA.Attachments.SaveBlob(attachment.Id, blob);

            SyncCycleResult uploadResult = await cycleA.Run();
            SyncCycleResult downloadResult = await cycleB.Run();

            Assert.Equal(1, uploadResult.BlobsUploaded);
            Assert.Equal(1, downloadResult.BlobsDownloaded);
            Assert.Equal("file.png", deviceB.Attachments.Get(attachment.Id).Filename);
            Assert.Equal(blob, deviceB.Attachments.GetBlob(attachment.Id));
        }

        // Regression probe for real-world uploads: a batch big enough to span many
        // network/JSON buffers, with multi-byte and escaped characters in every
        // payload - small ASCII fixtures never exercised those paths.
        [Fact]
        public async Task LargeMultiByteBatchSurvivesTheRoundTrip()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using TestDb deviceB = new TestDb();
            using HttpClient httpA = new HttpClient();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                builder.Append("emoji \U0001F680\U0001F600 café Zürich 日本語 ").Append('—').Append(" \"quotes\" \\ backslash `code` [[link target]]\n");
            }
            string body = builder.ToString();

            for (int i = 0; i < 120; i++)
            {
                deviceA.Notes.Save(Items.Note($"Note {i} é\U0001F343", body + $"tail {i}"));
            }

            SyncCycleResult uploadResult = await cycleA.Run();
            SyncCycleResult downloadResult = await cycleB.Run();

            Assert.Equal(120, uploadResult.Uploaded);
            Assert.Equal(120, downloadResult.Applied);

            foreach (Note original in deviceA.Notes.GetAll())
            {
                Note mirrored = deviceB.Notes.Get(original.Id);
                Assert.Equal(original.Title, mirrored.Title);
                Assert.Equal(original.Body, mirrored.Body);
            }
        }

        // The redeployed-stack scenario: a client with fully synced state meets a
        // brand-new server instance. The id mismatch must trigger the adoption reset
        // and the full history must reach the new server - not crash on foreign seqs.
        [Fact]
        public async Task SwitchingToAFreshServerAdoptsAndReuploadsEverything()
        {
            using ServerFixture oldServer = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using HttpClient httpA = new HttpClient();
            SyncCycle oldCycle = CycleFor(deviceA, oldServer, httpA);

            Note first = Items.Note("First", "body one");
            Note second = Items.Note("Second", "body two");
            deviceA.Notes.Save(first);
            deviceA.Notes.Save(second);
            await oldCycle.Run();

            Note edited = deviceA.Notes.Get(first.Id);
            edited.Title = "First edited";
            deviceA.Notes.Save(edited);
            await oldCycle.Run();

            string oldServerId = new ServerStateRepository(oldServer.ConnectionManager).GetServerId();
            Assert.Equal(oldServerId, deviceA.SyncState.Get().ServerId);
            Assert.Empty(deviceA.ChangeLog.GetPending());

            using ServerFixture newServer = new ServerFixture();
            using HttpClient httpNew = new HttpClient();
            SyncCycle newCycle = CycleFor(deviceA, newServer, httpNew);
            SyncCycleResult adoption = await newCycle.Run();

            string newServerId = new ServerStateRepository(newServer.ConnectionManager).GetServerId();
            Assert.True(adoption.Adopted);
            Assert.NotEqual(oldServerId, newServerId);
            Assert.Equal(newServerId, deviceA.SyncState.Get().ServerId);
            Assert.Empty(deviceA.ChangeLog.GetPending());

            using TestDb deviceB = new TestDb();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleB = CycleFor(deviceB, newServer, httpB);
            await cycleB.Run();

            Assert.Equal("First edited", deviceB.Notes.Get(first.Id).Title);
            Assert.Equal("Second", deviceB.Notes.Get(second.Id).Title);
        }

        // Same server id but the monotonic allocator is behind the client's cursor:
        // the instance was restored from an older backup. The rollback guard must
        // trigger the same adoption reset so the lost history is restored.
        [Fact]
        public async Task ServerRestoredFromOlderBackupTriggersAdoption()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using HttpClient httpA = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);

            Note note = Items.Note("Survives the rollback");
            deviceA.Notes.Save(note);
            await cycleA.Run();
            Assert.True(deviceA.SyncState.Get().LastSeenSeq > 0);

            using (Microsoft.Data.Sqlite.SqliteConnection connection = server.ConnectionManager.CreateConnection())
            using (Microsoft.Data.Sqlite.SqliteCommand rollback = connection.CreateCommand())
            {
                rollback.CommandText = @"
                    DELETE FROM change_log;
                    DELETE FROM note;
                    UPDATE server_state SET last_assigned_seq = 0;";
                rollback.ExecuteNonQuery();
            }

            SyncCycleResult adoption = await cycleA.Run();

            Assert.True(adoption.Adopted);
            Assert.Empty(deviceA.ChangeLog.GetPending());

            using TestDb deviceB = new TestDb();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);
            await cycleB.Run();

            Assert.Equal("Survives the rollback", deviceB.Notes.Get(note.Id).Title);
        }

        private static SyncCycle CycleFor(TestDb db, ServerFixture server, HttpClient http)
        {
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            SyncServerClient client = new SyncServerClient(http, server.BaseUrl, ServerFixture.Token);
            return new SyncCycle(db.ConnectionManager, db.DeviceId, client);
        }
    }
}
