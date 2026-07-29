using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpSaveResponse
    {
        public string UpdatedAt { get; set; }
    }
}
