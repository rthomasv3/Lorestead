using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class CreateNoteResponse
{
    public Note Note { get; set; }
}
