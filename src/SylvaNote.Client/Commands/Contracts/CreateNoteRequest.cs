namespace SylvaNote.Client.Commands.Contracts;

public sealed class CreateNoteRequest
{
    public string ParentId { get; set; }
    public string Title { get; set; }
    public bool Template { get; set; }
}
