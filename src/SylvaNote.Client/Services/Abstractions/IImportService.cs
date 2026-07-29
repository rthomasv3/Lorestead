using SylvaNote.Client.Commands.Contracts;

namespace SylvaNote.Client.Services.Abstractions;

public interface IImportService
{
    ImportPreflightResponse PickFile(PreviewImportRequest request);

    ImportPreflightResponse PickFolder(PreviewImportRequest request);

    // Recomputes the preflight for the already-picked source - the dialog calls
    // this when the destination changes, since the merge scope follows it.
    ImportPreflightResponse Preview(PreviewImportRequest request);

    RunImportResponse Run(RunImportRequest request);
}
