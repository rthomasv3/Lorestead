using Galdr.Native;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Commands;

internal static class UpdateCommands
{
    public static GaldrBuilder AddUpdateCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getUpdateStatus", (IUpdateService updates) => updates.GetStatus());
        builder.AddFunction("checkForUpdate", async (IUpdateService updates) => await updates.CheckForUpdate());
        builder.AddFunction("downloadUpdate", async (IUpdateService updates) => await updates.DownloadUpdate());
        builder.AddFunction("applyUpdateAndRestart", (IUpdateService updates) => updates.ApplyUpdateAndRestart());
        return builder;
    }
}
