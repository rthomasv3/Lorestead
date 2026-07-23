using System.Collections.Generic;
using SylvaNote.Core.Search;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class SearchTasksResponse
{
    public List<TaskSearchResult> Results { get; set; }
}
