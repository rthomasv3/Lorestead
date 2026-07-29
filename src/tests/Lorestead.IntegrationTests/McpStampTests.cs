using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lorestead.Core.DataAccess.Migrations;
using Lorestead.Core.Entities;
using Lorestead.Core.Mcp;
using Lorestead.Core.Mcp.Contracts;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    // Server-side MCP writes: repository saves append seq-NULL entries, the stamper
    // promotes them into the /changes feed.
    public sealed class McpStampTests : IDisposable
    {
        private readonly TestDb _db;
        private readonly ServerChangeStamper _stamper;

        public McpStampTests()
        {
            _db = new TestDb(MigrationSets.Server());
            _stamper = new ServerChangeStamper(_db.ConnectionManager, historyRetention: 50);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task StampedMcpWritesEnterTheChangesFeed()
        {
            McpToolService tools = new McpToolService(_db.ConnectionManager, "server-mcp", () =>
            {
                _stamper.StampPending();
                return Task.CompletedTask;
            });

            McpCreateResponse created = await tools.CreateNote("From agent", "agent body", null);
            await tools.AppendToNote(created.Id, "more");

            Assert.Empty(_db.ChangeLog.GetPending());
            List<ChangeLogEntry> feed = _db.ChangeLog.GetAfter(0, 10);
            Assert.Equal(2, feed.Count);
            Assert.Equal(1, feed[0].Seq);
            Assert.Equal(2, feed[1].Seq);
            Assert.All(feed, e => Assert.Equal("server-mcp", e.DeviceId));
            Assert.All(feed, e => Assert.False(e.SupersededConcurrent));
            Assert.Equal("agent body\n\nmore", PayloadJson.Deserialize<Note>(feed[1].Payload).Body);
        }

        [Fact]
        public async Task StampReappliesOverAnInterleavedClientUpload()
        {
            // No afterWrite hook: the entry stays pending, simulating a client upload
            // being ingested between the tool write and the stamp.
            McpToolService tools = new McpToolService(_db.ConnectionManager, "server-mcp");
            McpCreateResponse created = await tools.CreateNote("Agent title", null, null);

            Note clientVersion = _db.Notes.Get(created.Id);
            clientVersion.Title = "Client title";
            ChangeIngestor ingestor = new ChangeIngestor(_db.ConnectionManager);
            ingestor.Ingest(new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    ItemType = ItemTypes.Note,
                    ItemId = created.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(clientVersion),
                    DeviceId = "0198c0de-aaaa-7000-8000-00000000dev1",
                    ChangedAt = "2026-07-24T10:00:00.0000001Z",
                },
            });

            long maxSeq = _stamper.StampPending();

            // The MCP entry got the higher seq, so LWW says its payload wins - state
            // must agree with what a pulling client will end up applying.
            Assert.Equal(2, maxSeq);
            List<ChangeLogEntry> feed = _db.ChangeLog.GetAfter(0, 10);
            Assert.Equal(2, feed.Count);
            Assert.Equal("server-mcp", feed[1].DeviceId);
            Assert.True(feed[1].SupersededConcurrent);
            Assert.Equal("Agent title", _db.Notes.Get(created.Id).Title);
        }
    }
}
