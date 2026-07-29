using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpBoardResponse
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<McpColumnTasks> Columns { get; set; } = new List<McpColumnTasks>();
    }
}
