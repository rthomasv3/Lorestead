using System.Collections.Generic;
using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpBoardSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<McpColumnSummary> Columns { get; set; } = new List<McpColumnSummary>();
    }
}
