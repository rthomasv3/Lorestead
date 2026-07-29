namespace Lorestead.Client.Commands.Contracts;

public sealed class ImportPreflightResponse
{
    // False when the user cancelled the picker (or no source is selected).
    public bool Selected { get; set; }

    public string Path { get; set; }

    public int NoteCount { get; set; }

    public int CreatedCount { get; set; }

    public int MergedCount { get; set; }

    public int SkippedCount { get; set; }

    public int AttachmentCount { get; set; }

    public int TemplateCount { get; set; }
}
