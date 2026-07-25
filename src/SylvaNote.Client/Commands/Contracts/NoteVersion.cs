namespace SylvaNote.Client.Commands.Contracts;

// One history card's worth of a note. Trimmed to what history actually shows and
// restores - parent_id, position, type and deleted are in the stored payload but
// are deliberately not surfaced, because a restore never touches them
// (decisions.md).
public sealed class NoteVersion
{
    public long Id { get; set; }
    public string ChangedAt { get; set; }
    public string DeviceId { get; set; }
    public bool SupersededConcurrent { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
}
