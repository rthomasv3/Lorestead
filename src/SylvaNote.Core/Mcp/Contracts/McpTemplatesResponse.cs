using System.Collections.Generic;
using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTemplatesResponse
    {
        public List<McpTemplateSummary> Templates { get; set; } = new List<McpTemplateSummary>();
    }
}
