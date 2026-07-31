using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Sync;

namespace Lorestead.Server.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        // MinVer stamps the informational version from the git tag at build time
        // (Phase 9); until then this reads the SDK default.
        string appVersion = typeof(StatusEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        // ServerId and LastAssignedSeq drive the client's server-adoption check.
        // LastAssignedSeq (not MAX(seq)) because the allocator is monotonic while
        // the log tail can be pruned - a legitimate client cursor can never be
        // ahead of it, so ahead means the server lost history.
        app.MapGet("/status", (ServerStateRepository serverState) => new StatusResponse
        {
            AppVersion = appVersion,
            ProtocolVersion = SyncProtocol.Version,
            ServerId = serverState.GetServerId(),
            LastAssignedSeq = serverState.GetLastAssignedSeq(),
        });
    }
}
