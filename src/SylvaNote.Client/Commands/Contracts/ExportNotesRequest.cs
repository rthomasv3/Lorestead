using SylvaNote.Core.Export;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class ExportNotesRequest
{
    // Ignored for the All scope.
    public string NoteId { get; set; }

    public ExportScope Scope { get; set; }
}
