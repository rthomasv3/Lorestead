namespace SylvaNote.Client.Commands.Contracts;

public sealed class RenameAttachmentRequest
{
    public string Id { get; set; }
    public string Filename { get; set; }
}
