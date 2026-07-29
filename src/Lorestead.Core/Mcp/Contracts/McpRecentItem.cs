using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpRecentItem
    {
        // "note" or "task" - notes and tasks share one time-ordered list so an agent
        // sees a single activity timeline rather than two lists it has to merge.
        public string Type { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Breadcrumb { get; set; }
        public string UpdatedAt { get; set; }
    }
}
