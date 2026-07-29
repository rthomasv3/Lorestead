using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetBoardsResponse
{
    public List<Board> Boards { get; set; }
}
