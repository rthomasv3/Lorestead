using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpSearchResponse
    {
        public List<McpNoteHit> Notes { get; set; } = new List<McpNoteHit>();
        public List<McpTaskHit> Tasks { get; set; } = new List<McpTaskHit>();
        public List<McpBoardHit> Boards { get; set; } = new List<McpBoardHit>();
    }
}
