using System.Diagnostics.CodeAnalysis;
using GaldrJson;
using GaldrJson.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Mcp;
using SylvaNote.Core.Sync;
using SylvaNote.Server.Endpoints;
using SylvaNote.Server.Services;

namespace SylvaNote.Server;

// The whole app minus environment concerns (env vars, DB open, Run) so integration
// tests can boot the real pipeline in-process on a random port.
public static class ServerApp
{
    // Server-side writes in these endpoints never append outbox entries.
    private const string ServerDeviceId = "server";
    // MCP tool writes DO ride the outbox path and get stamped; this id marks agent
    // edits in the change log (features/mcp.md).
    private const string McpDeviceId = "server-mcp";

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Array.CreateInstance in framework DI internals, reached via AddMcpServer - the Phase 0 spike runtime-proved this exact path under Native AOT (decisions.md).")]
    public static WebApplication Create(ServerConfig config, ConnectionManager connectionManager, string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Blob uploads: the client caps attachments at 100 MB; Kestrel's default
            // body limit (~28 MB) would reject them.
            options.Limits.MaxRequestBodySize = 128 * 1024 * 1024;
        });
        builder.Services.AddGaldrJson(new GaldrJsonOptions
        {
            PropertyNamingPolicy = PropertyNamingPolicy.CamelCase,
        });
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(connectionManager);
        builder.Services.AddSingleton<ChangeLogRepository>();
        builder.Services.AddSingleton<ServerStateRepository>();
        builder.Services.AddSingleton(new ChangeIngestor(connectionManager, config.HistoryRetention));
        builder.Services.AddSingleton(new AttachmentRepository(connectionManager, ServerDeviceId));

        // Built by hand (not from DI) because the MCP tool set needs the broadcaster
        // and stamper before the container exists.
        SyncHintBroadcaster broadcaster = new SyncHintBroadcaster();
        builder.Services.AddSingleton(broadcaster);
        ServerChangeStamper stamper = new ServerChangeStamper(connectionManager, config.HistoryRetention);
        McpToolService mcpTools = new McpToolService(
            connectionManager,
            McpDeviceId,
            async () => await broadcaster.Broadcast(stamper.StampPending()));
        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools(McpToolRegistry.CreateTools(mcpTools));

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.UseBearerAuth(config);
        app.MapStatusEndpoints();
        app.MapChangeEndpoints();
        app.MapAttachmentEndpoints();
        app.MapSyncSocket();
        app.MapMcp("/mcp");
        return app;
    }
}
