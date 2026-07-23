using System.Collections.Generic;
using GaldrJson;

namespace SylvaNote.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class TaskItem
    {
        public string Id { get; set; }
        public string ColumnId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Position { get; set; }
        public bool Deleted { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }

        // Linked notes (task_note rows) ride the task payload so links sync; additive
        // link semantics live at the MCP/service layer, this is full item state.
        public List<string> NoteIds { get; set; } = new List<string>();
    }
}
