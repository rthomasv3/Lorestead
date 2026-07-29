using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTreeNode
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string UpdatedAt { get; set; }
        public List<McpTreeNode> Children { get; set; } = new List<McpTreeNode>();
    }
}
