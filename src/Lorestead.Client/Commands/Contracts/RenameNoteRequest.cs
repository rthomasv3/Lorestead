namespace Lorestead.Client.Commands.Contracts;

public sealed class RenameNoteRequest
{
    public string Id { get; set; }
    public string Title { get; set; }
}
