using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpMoveResponse
    {
        public string UpdatedAt { get; set; }
        public string ColumnId { get; set; }

        // Where the task actually landed: a requested index past the end (or a
        // negative one) is clamped, so the caller cannot infer this from its request.
        public int Index { get; set; }
    }
}
