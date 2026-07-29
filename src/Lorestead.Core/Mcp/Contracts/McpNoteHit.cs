using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpNoteHit
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Breadcrumb { get; set; }
        public string Snippet { get; set; }
        public string UpdatedAt { get; set; }
    }
}
