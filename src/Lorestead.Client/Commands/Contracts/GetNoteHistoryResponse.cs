using System.Collections.Generic;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetNoteHistoryResponse
{
    public List<NoteVersion> Versions { get; set; }
}
