using System.Collections.Generic;
using SylvaNote.Core.Search;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class SearchNotesResponse
{
    public List<SearchResult> Results { get; set; }
}
