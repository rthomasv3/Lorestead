namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetAttachmentDataResponse
{
    public string Filename { get; set; }
    public string MimeType { get; set; }
    public string DataBase64 { get; set; }
}
