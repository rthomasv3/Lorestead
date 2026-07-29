using Lorestead.Client.Commands.Contracts;

namespace Lorestead.Client.Services.Abstractions;

public interface IExportService
{
    ExportNotesResponse Export(ExportNotesRequest request);
}
