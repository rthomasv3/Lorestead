using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetBoardResponse
{
    public List<BoardColumn> Columns { get; set; }
    public List<TaskSummary> Tasks { get; set; }
}
