namespace Lorestead.Client.Commands.Contracts;

public sealed class RestoreNoteRequest
{
    public string Id { get; set; }
    public bool WithAncestors { get; set; }
}
