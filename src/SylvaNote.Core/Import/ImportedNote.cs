using SylvaNote.Core.Entities;

namespace SylvaNote.Core.Import
{
    public sealed class ImportedNote
    {
        public string Id { get; set; }

        // Null for a merge (the note stays where it is) and for template roots.
        public string ParentId { get; set; }

        public string Title { get; set; }

        // Links already rewritten to native note:// and attachment:// form.
        public string Body { get; set; }

        // Round-trip ISO strings from the front matter; null when absent or
        // unparseable, in which case the applier stamps now. Only creates use them -
        // a merge keeps its created date and gets a fresh updated stamp.
        public string CreatedAt { get; set; }

        public string UpdatedAt { get; set; }

        public NoteType Type { get; set; }

        public ImportAction Action { get; set; }

        // The file carried a valid sylvanote-id, whatever the merge outcome - this is
        // what the preflight's "N with SylvaNote ids" counts.
        public bool HadFrontMatterId { get; set; }

        public string Path { get; set; }
    }
}
