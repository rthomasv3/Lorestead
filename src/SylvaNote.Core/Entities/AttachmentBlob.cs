namespace SylvaNote.Core.Entities
{
    public sealed class AttachmentBlob
    {
        public string AttachmentId { get; set; }
        public byte[] Data { get; set; }
    }
}
