using System;
using System.IO;
using System.Reflection;
using Galdr.Native;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Commands;

internal static class SystemCommands
{
    public static GaldrBuilder AddSystemCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getAbout", () => new GetAboutResponse
        {
            AppName = "Lorestead",
            Version = GetVersion(),
        });

        builder.AddFunction("getPlatform", () => new GetPlatformResponse
        {
            Mobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS(),
        });

        builder.AddFunction("getLog", (ILoggingService logger) => new GetLogResponse
        {
            Text = logger.ReadLog(),
        });

        builder.AddFunction("getThirdPartyNotices", () => new GetThirdPartyNoticesResponse
        {
            Text = GetThirdPartyNotices(),
        });

        return builder;
    }

    private static string GetThirdPartyNotices()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        return File.Exists(path) ? File.ReadAllText(path) : "THIRD-PARTY-NOTICES.txt was not found beside the application.";
    }

    private static string GetVersion()
    {
        // Auto-stamped by the build (MinVer); never manually synced.
        AssemblyInformationalVersionAttribute attribute =
            typeof(SystemCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute == null ? "dev" : attribute.InformationalVersion;
    }
}
