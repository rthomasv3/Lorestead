namespace SylvaNote.Core.Mcp
{
    // Raw blob hand-off to the registration layer, which picks the MCP content type
    // by mime - never serialized as JSON.
    public sealed class McpAttachmentData
    {
        public string Id { get; set; }
        public string Filename { get; set; }
        public string MimeType { get; set; }
        public byte[] Data { get; set; }
    }
}
