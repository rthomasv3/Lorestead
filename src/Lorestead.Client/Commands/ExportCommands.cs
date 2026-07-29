using Galdr.Native;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Commands;

internal static class ExportCommands
{
    public static GaldrBuilder AddExportCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("exportNotes", (ExportNotesRequest request, IExportService export) => export.Export(request));
        return builder;
    }
}
