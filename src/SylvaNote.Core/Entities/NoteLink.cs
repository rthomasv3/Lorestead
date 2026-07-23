namespace SylvaNote.Core.Entities
{
    public sealed class NoteLink
    {
        public string FromNoteId { get; set; }
        public string FromTaskId { get; set; }
        public string ToNoteId { get; set; }
    }
}
