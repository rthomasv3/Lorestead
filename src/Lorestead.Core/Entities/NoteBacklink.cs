namespace Lorestead.Core.Entities
{
    // How a source reaches the note. A task can do both at once - mention it in its
    // body and carry it in its linked-notes list - which is why this is one field
    // with three values rather than two cards.
    public static class BacklinkVia
    {
        public const string Body = "body";
        public const string Link = "link";
        public const string Both = "both";
    }

    // A resolved backlink source, ready to render as a card. Exactly one of
    // NoteId/TaskId is set. Board/column names come along for task sources so the
    // card can say where the task lives without loading the board (the
    // task-search-result pattern). Snippet is empty for link-only sources - there
    // is no body text to quote.
    public sealed class NoteBacklink
    {
        public string NoteId { get; set; }
        public string TaskId { get; set; }
        public string Title { get; set; }
        public string Snippet { get; set; }
        public string Via { get; set; }
        public string ColumnName { get; set; }
        public string BoardId { get; set; }
        public string BoardName { get; set; }
    }
}
