using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class CreateColumnResponse
{
    public BoardColumn Column { get; set; }
}
