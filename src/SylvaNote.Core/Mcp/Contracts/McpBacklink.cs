using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    // Exactly one of NoteId/TaskId is set - mirrors the note_link exclusive arc.
    [GaldrJsonSerializable]
    public sealed class McpBacklink
    {
        public string NoteId { get; set; }
        public string TaskId { get; set; }
        public string Title { get; set; }
    }
}
