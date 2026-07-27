using System;
using System.IO;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.DataAccess.Migrations;
using SylvaNote.Core.Entities;
using SylvaNote.Core.FirstRun;
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
            bool created = connectionManager.Open(LocalDataPaths.GetDatabasePath(dataDirectory), MigrationSets.Client());
            SyncState state = new SyncStateRepository(connectionManager).EnsureInitialized();

            // An agent-first install is a real path for this app, so the binary that
            // created the database is the one that seeds it (decisions.md).
            if (created)
            {
                FirstRunSeeder.Seed(connectionManager, state.DeviceId);
            }

            // The -mcp suffix marks agent edits in the change log while keeping the
            // originating device recognizable (features/mcp.md). Retention comes from
            // the shared DB so agent writes cap history exactly as the client's do.
            ApplicationSettings settings = new SettingsRepository(connectionManager).GetApplication();
            McpToolService tools = new McpToolService(
                connectionManager,
                state.DeviceId + "-mcp",
                historyRetention: settings.HistoryRetention);

            // A debug build is one a developer registered against their own machine
            // while changing this code, so it says so rather than passing for an
            // installed one. Read from the build config, not from configuration a
            // client could get wrong.
#if DEBUG
            string build = "development build";
#else
            string build = "release build";
#endif

            await using McpServer server = McpServer.Create(
                new StdioServerTransport("SylvaNote"),
                new McpServerOptions
                {
                    ServerInfo = new Implementation
                    {
                        Name = "sylvanote",
                        Title = "SylvaNote (" + build + ")",
                        Version = typeof(Program).Assembly.GetName().Version.ToString(),
                    },
                    // Which database this is talking to is the one thing a caller
                    // cannot see and the one thing that has actually gone wrong
                    // before (decisions.md), so the handshake states it outright.
                    ServerInstructions =
                        "SylvaNote notes and boards, " + build + ", reading and writing the local database at "
                        + dataDirectory + ". Edits land in the same database the desktop client is using and "
                        + "appear there without a restart; they are attributed to this device with an -mcp suffix "
                        + "in the note history.",
                    ToolCollection = [.. McpToolRegistry.CreateTools(tools)],
                });
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
