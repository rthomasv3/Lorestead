namespace Lorestead.Client.Commands.Contracts;

public sealed class ExportNotesResponse
{
    // False when the user cancelled the save dialog.
    public bool Saved { get; set; }

    public string Path { get; set; }

    public int NoteCount { get; set; }
}
