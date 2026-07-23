using System.Collections.Generic;
using SylvaNote.Core.Search;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class SearchBoardsResponse
{
    public List<SearchResult> Results { get; set; }
}
