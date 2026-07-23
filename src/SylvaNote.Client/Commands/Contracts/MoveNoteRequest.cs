namespace SylvaNote.Client.Commands.Contracts;

// PreviousId/NextId are the rendered neighbors at the drop location (null at an
// edge) - the backend derives the fractional position from their keys. Template
// reflects the destination section: true only when dropped directly under the
// Templates virtual node.
public sealed class MoveNoteRequest
{
    public string Id { get; set; }
    public string ParentId { get; set; }
    public string PreviousId { get; set; }
    public string NextId { get; set; }
    public bool Template { get; set; }
}
