using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpLinkedNote
    {
        public string Id { get; set; }
        public string Title { get; set; }
    }
}
