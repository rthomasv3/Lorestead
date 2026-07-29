using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpAttachmentInfo
    {
        public string Id { get; set; }
        public string Filename { get; set; }
        public string MimeType { get; set; }
        public long SizeBytes { get; set; }
    }
}
