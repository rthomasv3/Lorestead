using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.Export
{
    public sealed class ExportSource
    {
        // Every note that is in scope. A note whose parent is absent from this list
        // becomes a root of the exported tree, which is what makes a subtree export
        // and a whole-tree export the same code path.
        public List<Note> Notes { get; set; }

        public List<Attachment> Attachments { get; set; }

        // Only the whole-tree export wraps template roots in a Templates/ folder -
        // exporting one template subtree on its own puts it at the root, because the
        // user picked that note, not the section.
        public bool GroupTemplates { get; set; }
    }
}
