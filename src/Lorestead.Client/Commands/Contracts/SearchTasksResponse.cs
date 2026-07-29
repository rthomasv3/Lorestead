using System.Collections.Generic;
using Lorestead.Core.Search;

namespace Lorestead.Client.Commands.Contracts;

public sealed class SearchTasksResponse
{
    public List<TaskSearchResult> Results { get; set; }
}
