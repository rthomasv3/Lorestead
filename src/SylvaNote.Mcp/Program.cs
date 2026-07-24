using System;
using System.IO;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.DataAccess.Migrations;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Mcp;

namespace SylvaNote.Mcp;

internal static class Program
{
    private static async Task<int> Main()
    {
        int exitCode = 0;

        try
        {
            SQLitePCL.Batteries_V2.Init();

            // Must resolve the same file as the client's Config.Create("SylvaNote") -
            // agent hosts spawn this binary with zero config and it shares the client
            // DB through WAL (decisions.md multi-process rules). SYLVANOTE_DATA_DIR
            // overrides for testing, mirroring the server's variable.
            string dataDirectory = Environment.GetEnvironmentVariable("SYLVANOTE_DATA_DIR");
            if (string.IsNullOrEmpty(dataDirectory))
            {
                dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SylvaNote");
            }
            Directory.CreateDirectory(dataDirectory);

            ConnectionManager connectionManager = new ConnectionManager();
            connectionManager.Open(Path.Combine(dataDirectory, "sylvanote.db"), MigrationSets.Client());
            SyncState state = new SyncStateRepository(connectionManager).EnsureInitialized();

            // The -mcp suffix marks agent edits in the change log while keeping the
            // originating device recognizable (features/mcp.md).
            McpToolService tools = new McpToolService(connectionManager, state.DeviceId + "-mcp");

            await using McpServer server = McpServer.Create(
                new StdioServerTransport("SylvaNote"),
                new McpServerOptions { ToolCollection = [.. McpToolRegistry.CreateTools(tools)] });
            await server.RunAsync();
        }
        catch (Exception ex)
        {
            // stdout carries the MCP protocol; diagnostics go to stderr only.
            Console.Error.WriteLine(ex);
            exitCode = 1;
        }

        return exitCode;
    }
}
