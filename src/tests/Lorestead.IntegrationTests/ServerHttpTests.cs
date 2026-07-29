using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    public sealed class ServerHttpTests
    {
        private const string DeviceA = "0198c0de-aaaa-7000-8000-00000000dev1";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task StatusRequiresTheBearerToken()
        {
            using ServerFixture server = new ServerFixture();

            using HttpClient anonymous = new HttpClient();
            HttpResponseMessage denied = await anonymous.GetAsync($"{server.BaseUrl}/status", Token);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            HttpResponseMessage allowed = await server.Client.GetAsync("/status", Token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            StatusResponse status = PayloadJson.Deserialize<StatusResponse>(await allowed.Content.ReadAsStringAsync(Token));
            Assert.Equal(SyncProtocol.Version, status.ProtocolVersion);
        }

        [Fact]
        public async Task ChangesRoundTripOverHttp()
        {
            using ServerFixture server = new ServerFixture();
            Note note = Items.Note("Wire note", "over http");

            HttpResponseMessage posted = await PostChanges(server, Upsert(note, "2026-07-23T10:00:00.0000001Z"));
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
            UploadChangesResponse uploaded = PayloadJson.Deserialize<UploadChangesResponse>(await posted.Content.ReadAsStringAsync(Token));
            Assert.Equal(1, Assert.Single(uploaded.Results).Seq);

            HttpResponseMessage pulled = await server.Client.GetAsync("/changes?since=0", Token);
            ChangesPageResponse page = PayloadJson.Deserialize<ChangesPageResponse>(await pulled.Content.ReadAsStringAsync(Token));
            ChangeLogEntry entry = Assert.Single(page.Entries);
            Assert.Equal(note.Id, entry.ItemId);
            Assert.Equal(1, page.MaxSeq);
            Assert.Equal("Wire note", PayloadJson.Deserialize<Note>(entry.Payload).Title);
        }

        [Fact]
        public async Task BlobPutThenGetRoundTrips()
        {
            using ServerFixture server = new ServerFixture();
            Note owner = Items.Note("Owner");
            Attachment attachment = Items.Attachment(noteId: owner.Id);
            byte[] blob = Encoding.UTF8.GetBytes("blob bytes over the wire");

            await PostChanges(server, Upsert(owner, "2026-07-23T10:00:00.0000001Z"));
            await PostChanges(server, AttachmentUpsert(attachment, "2026-07-23T10:00:00.0000002Z"));

            HttpResponseMessage missing = await server.Client.GetAsync($"/attachments/{attachment.Id}/blob", Token);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            HttpResponseMessage unknown = await server.Client.PutAsync($"/attachments/{Items.NewId()}/blob", new ByteArrayContent(blob), Token);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

            HttpResponseMessage put = await server.Client.PutAsync($"/attachments/{attachment.Id}/blob", new ByteArrayContent(blob), Token);
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

            HttpResponseMessage got = await server.Client.GetAsync($"/attachments/{attachment.Id}/blob", Token);
            Assert.Equal(HttpStatusCode.OK, got.StatusCode);
            Assert.Equal("image/png", got.Content.Headers.ContentType.MediaType);
            Assert.Equal(blob, await got.Content.ReadAsByteArrayAsync(Token));
        }

        [Fact]
        public async Task CursorBelowTheWatermarkGets410ButFullPullsNever()
        {
            using ServerFixture server = new ServerFixture();
            await PostChanges(server, Upsert(Items.Note("A"), "2026-07-23T10:00:00.0000001Z"));
            await PostChanges(server, Upsert(Items.Note("B"), "2026-07-23T10:00:00.0000002Z"));

            using (Microsoft.Data.Sqlite.SqliteConnection connection = server.ConnectionManager.CreateConnection())
            using (Microsoft.Data.Sqlite.SqliteTransaction transaction = connection.BeginTransaction())
            {
                Lorestead.Core.DataAccess.ServerStateRepository.RaisePrunedThroughSeqWithin(connection, transaction, 2);
                transaction.Commit();
            }

            HttpResponseMessage behind = await server.Client.GetAsync("/changes?since=1", Token);
            Assert.Equal(HttpStatusCode.Gone, behind.StatusCode);

            // since=0 is a full pull from empty state - nothing can strand it.
            HttpResponseMessage fullPull = await server.Client.GetAsync("/changes?since=0", Token);
            Assert.Equal(HttpStatusCode.OK, fullPull.StatusCode);

            HttpResponseMessage caughtUp = await server.Client.GetAsync("/changes?since=2", Token);
            Assert.Equal(HttpStatusCode.OK, caughtUp.StatusCode);
        }

        [Fact]
        public async Task UploadBroadcastsAHintOnTheSocket()
        {
            using ServerFixture server = new ServerFixture();

            using ClientWebSocket socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {ServerFixture.Token}");
            Uri wsUri = new Uri(server.BaseUrl.Replace("http://", "ws://") + "/ws");
            await socket.ConnectAsync(wsUri, Token);

            await PostChanges(server, Upsert(Items.Note("Hinted"), "2026-07-23T10:00:00.0000001Z"));

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            byte[] buffer = new byte[256];
            WebSocketReceiveResult received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            SyncHint hint = PayloadJson.Deserialize<SyncHint>(Encoding.UTF8.GetString(buffer, 0, received.Count));

            Assert.Equal(1, hint.MaxSeq);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Token);
        }

        [Fact]
        public async Task SocketRejectsAWrongToken()
        {
            using ServerFixture server = new ServerFixture();

            using ClientWebSocket socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer wrong");
            Uri wsUri = new Uri(server.BaseUrl.Replace("http://", "ws://") + "/ws");

            await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(wsUri, Token));
        }

        private static async Task<HttpResponseMessage> PostChanges(ServerFixture server, ChangeLogEntry entry)
        {
            UploadChangesRequest request = new UploadChangesRequest { Entries = new List<ChangeLogEntry> { entry } };
            StringContent body = new StringContent(PayloadJson.Serialize(request), Encoding.UTF8, "application/json");
            return await server.Client.PostAsync("/changes", body, Token);
        }

        private static ChangeLogEntry Upsert(Note note, string changedAt)
        {
            note.CreatedAt = changedAt;
            note.UpdatedAt = changedAt;
            return new ChangeLogEntry
            {
                ItemType = ItemTypes.Note,
                ItemId = note.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(note),
                DeviceId = DeviceA,
                ChangedAt = changedAt,
            };
        }

        private static ChangeLogEntry AttachmentUpsert(Attachment attachment, string changedAt)
        {
            attachment.CreatedAt = changedAt;
            attachment.UpdatedAt = changedAt;
            return new ChangeLogEntry
            {
                ItemType = ItemTypes.Attachment,
                ItemId = attachment.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(attachment),
                DeviceId = DeviceA,
                ChangedAt = changedAt,
            };
        }
    }
}
