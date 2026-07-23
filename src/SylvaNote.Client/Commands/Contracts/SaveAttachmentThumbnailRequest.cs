namespace SylvaNote.Client.Commands.Contracts;

public sealed class SaveAttachmentThumbnailRequest
{
    public string Id { get; set; }
    public string DataBase64 { get; set; }
}
