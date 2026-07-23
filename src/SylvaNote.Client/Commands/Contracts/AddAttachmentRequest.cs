namespace SylvaNote.Client.Commands.Contracts;

// The blob crosses the bridge as base64 — acceptable for the 100 MB cap and keeps
// the command layer JSON-only; dedicated endpoints exist only on the sync server.
public sealed class AddAttachmentRequest
{
    public string NoteId { get; set; }
    public string Filename { get; set; }
    public string MimeType { get; set; }
    public string DataBase64 { get; set; }
}
