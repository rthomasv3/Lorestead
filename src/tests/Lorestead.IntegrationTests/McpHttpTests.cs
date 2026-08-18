using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Lorestead.Core.Mcp.Contracts;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    // The full MCP surface over the server's streamable HTTP endpoint: real Kestrel,
    // bearer auth, manual tool registration, GaldrJson results.
    public sealed class McpHttpTests
    {
        private static readonly string[] ExpectedTools =
        {
            "search", "list_note_tree", "list_recent", "get_note", "create_note", "update_note",
            "edit_note", "append_to_note", "list_boards", "get_board", "get_task", "create_task",
            "update_task", "move_task", "link_note_to_task", "list_templates",
            "create_template", "create_note_from_template", "get_attachment", "add_attachment",
        };

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task ToolInventoryMatchesTheSpec()
        {
            using ServerFixture server = new ServerFixture();
            await using McpClient client = await Connect(server);

            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Token);
            Assert.Equal(ExpectedTools.OrderBy(n => n), tools.Select(t => t.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task McpRequiresTheBearerToken()
        {
            using ServerFixture server = new ServerFixture();
            HttpClientTransport transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{server.BaseUrl}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            });

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: Token);
            });
        }

        [Fact]
        public async Task NoteWritesRoundTripAndEnterTheSyncFeed()
        {
            using ServerFixture server = new ServerFixture();
            await using McpClient client = await Connect(server);

            CallToolResult created = await client.CallToolAsync(
                "create_note",
                new Dictionary<string, object> { ["title"] = "Agent note", ["body"] = "from mcp" },
                cancellationToken: Token);
            Assert.NotEqual(true, created.IsError);
            McpCreateResponse createResponse = PayloadJson.Deserialize<McpCreateResponse>(TextOf(created));

            CallToolResult fetched = await client.CallToolAsync(
                "get_note",
                new Dictionary<string, object> { ["noteId"] = createResponse.Id },
                cancellationToken: Token);
            McpNoteResponse note = PayloadJson.Deserialize<McpNoteResponse>(TextOf(fetched));
            Assert.Equal("Agent note", note.Title);
            Assert.Equal("from mcp", note.Body);

            // The write must be stamped into the /changes feed so devices pull it.
            List<Lorestead.Core.Entities.ChangeLogEntry> feed =
                new Lorestead.Core.DataAccess.ChangeLogRepository(server.ConnectionManager).GetAfter(0, 10);
            Lorestead.Core.Entities.ChangeLogEntry entry = Assert.Single(feed);
            Assert.Equal("server-mcp", entry.DeviceId);
            Assert.Equal(createResponse.Id, entry.ItemId);
        }

        [Fact]
        public async Task ToolFailuresSurfaceTheRealMessage()
        {
            using ServerFixture server = new ServerFixture();
            await using McpClient client = await Connect(server);

            CallToolResult result = await client.CallToolAsync(
                "get_note",
                new Dictionary<string, object> { ["noteId"] = Items.NewId() },
                cancellationToken: Token);
            Assert.Equal(true, result.IsError);
            Assert.Contains("does not exist", TextOf(result));
        }

        [Fact]
        public async Task ImageAttachmentComesBackAsImageContent()
        {
            using ServerFixture server = new ServerFixture();
            await using McpClient client = await Connect(server);

            byte[] bytes = { 137, 80, 78, 71, 13, 10 };
            CallToolResult note = await client.CallToolAsync(
                "create_note",
                new Dictionary<string, object> { ["title"] = "Owner" },
                cancellationToken: Token);
            string noteId = PayloadJson.Deserialize<McpCreateResponse>(TextOf(note)).Id;

            CallToolResult added = await client.CallToolAsync(
                "add_attachment",
                new Dictionary<string, object>
                {
                    ["filename"] = "pixel.png",
                    ["mimeType"] = "image/png",
                    ["dataBase64"] = Convert.ToBase64String(bytes),
                    ["noteId"] = noteId,
                },
                cancellationToken: Token);
            string attachmentId = PayloadJson.Deserialize<McpCreateResponse>(TextOf(added)).Id;

            CallToolResult fetched = await client.CallToolAsync(
                "get_attachment",
                new Dictionary<string, object> { ["attachmentId"] = attachmentId },
                cancellationToken: Token);
            ImageContentBlock image = Assert.IsType<ImageContentBlock>(Assert.Single(fetched.Content));
            Assert.Equal("image/png", image.MimeType);
            // Data carries base64 text as bytes (SDK 1.4.x convention); DecodedData is
            // the raw binary.
            Assert.Equal(bytes, image.DecodedData.ToArray());
        }

        private static async Task<McpClient> Connect(ServerFixture server)
        {
            HttpClientTransport transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{server.BaseUrl}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {ServerFixture.Token}",
                },
            });
            return await McpClient.CreateAsync(transport, cancellationToken: Token);
        }

        private static string TextOf(CallToolResult result)
        {
            return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        }
    }
}
