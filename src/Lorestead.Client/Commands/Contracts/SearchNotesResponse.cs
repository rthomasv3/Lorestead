using System.Collections.Generic;
using Lorestead.Core.Search;

namespace Lorestead.Client.Commands.Contracts;

public sealed class SearchNotesResponse
{
    public List<SearchResult> Results { get; set; }
}
