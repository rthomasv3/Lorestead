using System.Reflection;
using Microsoft.AspNetCore.Builder;
using SylvaNote.Core.Sync;

namespace SylvaNote.Server.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        // MinVer stamps the informational version from the git tag at build time
        // (Phase 9); until then this reads the SDK default.
        string appVersion = typeof(StatusEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        app.MapGet("/status", () => new StatusResponse
        {
            AppVersion = appVersion,
            ProtocolVersion = SyncProtocol.Version,
        });
    }
}
