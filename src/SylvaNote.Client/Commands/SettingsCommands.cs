using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class SettingsCommands
{
    public static GaldrBuilder AddSettingsCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getSettings", (ISettingsService settings) => settings.GetSettings());
        builder.AddFunction("saveApplicationSettings", (SaveApplicationSettingsRequest request, ISettingsService settings) => settings.SaveApplication(request));
        builder.AddFunction("saveEditorSettings", (SaveEditorSettingsRequest request, ISettingsService settings) => settings.SaveEditor(request));
        return builder;
    }
}
