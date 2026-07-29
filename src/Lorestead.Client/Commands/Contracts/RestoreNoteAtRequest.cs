namespace Lorestead.Client.Commands.Contracts;

// Drag-out-of-trash restore: the drop location is explicit, so no dialog.
public sealed class RestoreNoteAtRequest
{
    public string Id { get; set; }
    public string ParentId { get; set; }
    public string PreviousId { get; set; }
    public string NextId { get; set; }
}
