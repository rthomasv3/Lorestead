using Lorestead.Core.Export;

namespace Lorestead.Client.Commands.Contracts;

public sealed class ExportNotesRequest
{
    // Ignored for the All scope.
    public string NoteId { get; set; }

    public ExportScope Scope { get; set; }
}
