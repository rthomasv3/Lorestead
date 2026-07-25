using System.Collections.Generic;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetNoteHistoryResponse
{
    public List<NoteVersion> Versions { get; set; }
}
