using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTemplateSummary
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string UpdatedAt { get; set; }
    }
}
