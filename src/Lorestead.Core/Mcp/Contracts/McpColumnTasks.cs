using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpColumnTasks
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<McpTaskSummary> Tasks { get; set; } = new List<McpTaskSummary>();
    }
}
