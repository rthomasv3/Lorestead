using System.Collections.Generic;
using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpRecentResponse
    {
        public List<McpRecentItem> Items { get; set; } = new List<McpRecentItem>();
    }
}
