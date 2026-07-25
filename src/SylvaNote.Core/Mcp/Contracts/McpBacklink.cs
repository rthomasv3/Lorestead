using GaldrJson;

namespace SylvaNote.Core.Mcp.Contracts
{
    // Exactly one of NoteId/TaskId is set. Via says how the source reaches the note:
    // "body" (its markdown links to it), "link" (a task carrying it in its
    // linked-notes list), or "both".
    [GaldrJsonSerializable]
    public sealed class McpBacklink
    {
        public string NoteId { get; set; }
        public string TaskId { get; set; }
        public string Title { get; set; }
        public string Via { get; set; }
    }
}
