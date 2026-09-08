namespace Lorestead.Client.Commands.Contracts;

public sealed class ReadAttachmentFileResponse
{
    // Null for a path that is not a readable file, or one over the size limit -
    // the caller skips it the way an oversized dropped file is skipped.
    public string Filename { get; set; }
    public string MimeType { get; set; }
    public string DataBase64 { get; set; }
}
