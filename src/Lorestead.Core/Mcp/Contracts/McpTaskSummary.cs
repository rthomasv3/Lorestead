using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTaskSummary
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public List<string> Labels { get; set; } = new List<string>();
        public string UpdatedAt { get; set; }
    }
}
