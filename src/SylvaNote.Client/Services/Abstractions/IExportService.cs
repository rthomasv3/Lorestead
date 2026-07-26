using SylvaNote.Client.Commands.Contracts;

namespace SylvaNote.Client.Services.Abstractions;

public interface IExportService
{
    ExportNotesResponse Export(ExportNotesRequest request);
}
