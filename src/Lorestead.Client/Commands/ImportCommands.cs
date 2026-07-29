using Galdr.Native;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Commands;

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
