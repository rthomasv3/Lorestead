namespace SylvaNote.Client.Commands.Contracts;

public sealed class RestoreNoteVersionRequest
{
    public string NoteId { get; set; }
    public long VersionId { get; set; }
}
