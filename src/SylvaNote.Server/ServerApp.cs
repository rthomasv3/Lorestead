using GaldrJson;
using GaldrJson.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Sync;
using SylvaNote.Server.Endpoints;
using SylvaNote.Server.Services;

namespace SylvaNote.Server;

// The whole app minus environment concerns (env vars, DB open, Run) so integration
// tests can boot the real pipeline in-process on a random port.
public static class ServerApp
{
    // Server-side writes in these endpoints never append outbox entries; the real
    // server device identity story arrives with MCP (Phase 6).
    private const string ServerDeviceId = "server";

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
        builder.Services.AddSingleton<SyncHintBroadcaster>();

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.UseBearerAuth(config);
        app.MapStatusEndpoints();
        app.MapChangeEndpoints();
        app.MapAttachmentEndpoints();
        app.MapSyncSocket();
        return app;
    }
}
