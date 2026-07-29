namespace Lorestead.Client.Commands.Contracts;

// The blob crosses the bridge as base64 - acceptable for the 100 MB cap and keeps
// the command layer JSON-only; dedicated endpoints exist only on the sync server.
// Exactly one of NoteId/TaskId is set - attachments have a single owner (data.md).
public sealed class AddAttachmentRequest
{
    public string NoteId { get; set; }
    public string TaskId { get; set; }
    public string Filename { get; set; }
    public string MimeType { get; set; }
    public string DataBase64 { get; set; }
    // Frontend-generated image thumbnail (device-local derived cache); null for
    // non-images.
    public string ThumbnailBase64 { get; set; }
}
