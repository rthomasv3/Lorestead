using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class ExportCommands
{
    public static GaldrBuilder AddExportCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("exportNotes", (ExportNotesRequest request, IExportService export) => export.Export(request));
        return builder;
    }
}
