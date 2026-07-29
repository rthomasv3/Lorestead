using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpNoteTreeResponse
    {
        public List<McpTreeNode> Notes { get; set; } = new List<McpTreeNode>();
    }
}
