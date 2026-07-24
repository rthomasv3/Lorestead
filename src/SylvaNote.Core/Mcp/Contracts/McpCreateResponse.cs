using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpCreateResponse
    {
        public string Id { get; set; }
    }
}
