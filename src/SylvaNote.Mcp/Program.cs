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

            // Same resolver the client uses, so agent edits land in the client's DB
            // wherever this binary happens to be installed (decisions.md). Legacy
            // migration is deliberately client-only - a spawned binary can see a
            // virtualized AppData shadow instead of the real files.
            string dataDirectory = LocalDataPaths.ResolveDataDirectory();
            Directory.CreateDirectory(dataDirectory);

            ConnectionManager connectionManager = new ConnectionManager();
            connectionManager.Open(LocalDataPaths.GetDatabasePath(dataDirectory), MigrationSets.Client());
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
