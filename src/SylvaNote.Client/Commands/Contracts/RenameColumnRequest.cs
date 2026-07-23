namespace SylvaNote.Client.Commands.Contracts;

public sealed class RenameColumnRequest
{
    public string Id { get; set; }
    public string Name { get; set; }
}
