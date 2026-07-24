using System.Collections.Generic;
using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTreeNode
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public List<McpTreeNode> Children { get; set; } = new List<McpTreeNode>();
    }
}
