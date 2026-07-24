using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpBoardHit
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
