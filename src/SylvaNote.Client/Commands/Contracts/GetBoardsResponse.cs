using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetBoardsResponse
{
    public List<Board> Boards { get; set; }
}
