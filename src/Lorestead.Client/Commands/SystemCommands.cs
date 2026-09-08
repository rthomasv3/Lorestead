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

        // The webview has a console nobody can open on the desktop, so anything the
        // frontend needs to say about a session goes to the same log as the rest.
        builder.AddFunction("logMessage", (LogMessageRequest request, ILoggingService logger) =>
        {
            logger.Debug(string.IsNullOrEmpty(request.Source) ? "Frontend" : request.Source, request.Message ?? string.Empty);
            return new LogMessageResponse { Logged = true };
        });

        builder.AddFunction("getThirdPartyNotices", () => new GetThirdPartyNoticesResponse
        {
            Text = GetThirdPartyNotices(),
        });

        return builder;
    }

    private static string GetThirdPartyNotices()
    {
        // The embedded copy, not the file beside the binary: Android apps have no
        // loose files, and one read then serves every platform. The Content copy
        // still ships beside desktop builds for anyone browsing the install folder.
        string text = "THIRD-PARTY-NOTICES.txt is missing from this build.";
        using Stream stream = typeof(SystemCommands).Assembly.GetManifestResourceStream("THIRD-PARTY-NOTICES.txt");
        if (stream != null)
        {
            using StreamReader reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        return text;
    }

    private static string GetVersion()
    {
        // Auto-stamped by the build (MinVer); never manually synced.
        AssemblyInformationalVersionAttribute attribute =
            typeof(SystemCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute == null ? "dev" : attribute.InformationalVersion;
    }
}
