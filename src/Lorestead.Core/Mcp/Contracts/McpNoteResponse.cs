using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpNoteResponse
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public bool Deleted { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public List<McpAttachmentInfo> Attachments { get; set; } = new List<McpAttachmentInfo>();
        public List<McpBacklink> Backlinks { get; set; } = new List<McpBacklink>();
    }
}
