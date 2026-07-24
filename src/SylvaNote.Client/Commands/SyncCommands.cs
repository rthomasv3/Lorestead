using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class SyncCommands
{
    public static GaldrBuilder AddSyncCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getSyncStatus", (ISyncService sync) => sync.GetStatus());
        builder.AddFunction("saveSyncServerUrl", (SaveSyncServerUrlRequest request, ISyncService sync) => sync.SaveServerUrl(request));
        builder.AddFunction("saveSyncToken", (SaveSyncTokenRequest request, ISyncService sync) => sync.SaveToken(request));
        builder.AddFunction("syncNow", (ISyncService sync) => sync.SyncNow());
        return builder;
    }
}
