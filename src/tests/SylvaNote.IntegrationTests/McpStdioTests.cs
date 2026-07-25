using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SylvaNote.Core.Mcp.Contracts;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    // The real SylvaNote.Mcp binary over the real stdio transport, pointed at a
    // throwaway data dir via SYLVANOTE_DATA_DIR. Spawned by path (not a project
    // reference) because referencing the binary would pull bundle_e_sqlite3 into this
    // graph next to the server's bundle_e_sqlcipher (see TestSetup).
    public sealed class McpStdioTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task StdioBinaryServesTheFullToolSet()
        {
            string exe = FindBinary();
            Assert.SkipWhen(exe == null, "SylvaNote.Mcp.exe has not been built.");

            string dataDir = Path.Combine(Path.GetTempPath(), $"sylvanote-mcp-stdio-{Guid.NewGuid():N}");
            try
            {
                StdioClientTransport transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "SylvaNote",
                    Command = exe,
                    EnvironmentVariables = new Dictionary<string, string> { ["SYLVANOTE_DATA_DIR"] = dataDir },
                });

                await using (McpClient client = await McpClient.CreateAsync(transport, cancellationToken: Token))
                {
                    IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Token);
                    Assert.Equal(19, tools.Count);

                    CallToolResult created = await client.CallToolAsync(
                        "create_note",
                        new Dictionary<string, object> { ["title"] = "Stdio note", ["body"] = "over stdio" },
                        cancellationToken: Token);
                    Assert.NotEqual(true, created.IsError);
                    string noteId = PayloadJson.Deserialize<McpCreateResponse>(TextOf(created)).Id;

                    CallToolResult fetched = await client.CallToolAsync(
                        "get_note",
                        new Dictionary<string, object> { ["noteId"] = noteId },
                        cancellationToken: Token);
                    Assert.Equal("over stdio", PayloadJson.Deserialize<McpNoteResponse>(TextOf(fetched)).Body);
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(dataDir, recursive: true);
                }
                catch (IOException)
                {
                    // Temp-dir cleanup is best-effort; the OS temp cleaner gets the rest.
                }
            }
        }

        private static string FindBinary()
        {
            string root = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(root, @"..\..\..\..\..\SylvaNote.Mcp\bin\Debug\net10.0\SylvaNote.Mcp.exe")),
                Path.GetFullPath(Path.Combine(root, @"..\..\..\..\..\SylvaNote.Mcp\bin\Release\net10.0\SylvaNote.Mcp.exe")),
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private static string TextOf(CallToolResult result)
        {
            return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        }
    }
}
