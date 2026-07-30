using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Lorestead.Core.DataAccess;
using Lorestead.Core.DataAccess.Migrations;
using Lorestead.Core.Entities;
using Lorestead.Core.FirstRun;
using Lorestead.Core.Mcp;

namespace Lorestead.Mcp;

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
                new StdioServerTransport("Lorestead"),
                new McpServerOptions
                {
                    ServerInfo = new Implementation
                    {
                        Name = "lorestead",
                        Title = "Lorestead (" + build + ")",
                        // InformationalVersion carries the MinVer-stamped semver; the
                        // assembly version it would otherwise read stays Major.0.0.0.
                        Version = GetVersion(),
                    },
                    // Which database this is talking to is the one thing a caller
                    // cannot see and the one thing that has actually gone wrong
                    // before (decisions.md), so the handshake states it outright.
                    ServerInstructions =
                        "Lorestead notes and boards, " + build + ", reading and writing the local database at "
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

    private static string GetVersion()
    {
        AssemblyInformationalVersionAttribute attribute =
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute == null ? "dev" : attribute.InformationalVersion;
    }
}
