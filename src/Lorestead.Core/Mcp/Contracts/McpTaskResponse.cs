using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTaskResponse
    {
        public string Id { get; set; }
        public string BoardId { get; set; }
        public string BoardName { get; set; }
        public string ColumnId { get; set; }
        public string ColumnName { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public List<McpAttachmentInfo> Attachments { get; set; } = new List<McpAttachmentInfo>();
        public List<McpLinkedNote> LinkedNotes { get; set; } = new List<McpLinkedNote>();
    }
}
