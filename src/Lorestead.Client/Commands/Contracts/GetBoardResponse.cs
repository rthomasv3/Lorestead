using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetBoardResponse
{
    public List<BoardColumn> Columns { get; set; }
    public List<TaskSummary> Tasks { get; set; }
}
