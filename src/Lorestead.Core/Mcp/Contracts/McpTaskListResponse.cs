using System.Collections.Generic;
using GaldrJson;

namespace Lorestead.Core.Mcp.Contracts
{
    // list_tasks: one board's live tasks after the query and label filters, in
    // board order (column by column, then position). Column names ride along
    // so an agent can act on a hit without a get_board round trip.
    [GaldrJsonSerializable]
    public sealed class McpTaskListResponse
    {
        public string BoardId { get; set; }
        public string BoardName { get; set; }
        public List<McpTaskListItem> Tasks { get; set; } = new List<McpTaskListItem>();
    }
}
