namespace Lorestead.Client.Commands.Contracts;

public sealed class SearchNotesRequest
{
    public string Query { get; set; }
    public bool IncludeTrashed { get; set; }
}
