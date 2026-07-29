using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpBoardsResponse
    {
        public List<McpBoardSummary> Boards { get; set; } = new List<McpBoardSummary>();
    }
}
