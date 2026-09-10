using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    [GaldrJsonSerializable]
    public sealed class McpTaskListItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string ColumnId { get; set; }
        public string ColumnName { get; set; }
        public List<string> Labels { get; set; } = new List<string>();
        // Only when a query was given: the matched text, as in search.
        public string Snippet { get; set; }
        public string UpdatedAt { get; set; }
    }
}
