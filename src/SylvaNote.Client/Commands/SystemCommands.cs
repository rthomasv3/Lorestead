using System.Reflection;
using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class SystemCommands
{
    public static GaldrBuilder AddSystemCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getAbout", () => new GetAboutResponse
        {
            AppName = "SylvaNote",
            Version = GetVersion(),
        });

        builder.AddFunction("getLog", (ILoggingService logger) => new GetLogResponse
        {
            Text = logger.ReadLog(),
        });

        return builder;
    }

    private static string GetVersion()
    {
        // Auto-stamped by the build (MinVer from Phase 9 on); never manually synced.
        AssemblyInformationalVersionAttribute attribute =
            typeof(SystemCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute == null ? "dev" : attribute.InformationalVersion;
    }
}
