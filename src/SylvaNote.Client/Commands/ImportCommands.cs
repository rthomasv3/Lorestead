using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class ImportCommands
{
    public static GaldrBuilder AddImportCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("pickImportFile", (PreviewImportRequest request, IImportService import) => import.PickFile(request));
        builder.AddFunction("pickImportFolder", (PreviewImportRequest request, IImportService import) => import.PickFolder(request));
        builder.AddFunction("previewImport", (PreviewImportRequest request, IImportService import) => import.Preview(request));
        builder.AddFunction("runImport", (RunImportRequest request, IImportService import) => import.Run(request));
        return builder;
    }
}
