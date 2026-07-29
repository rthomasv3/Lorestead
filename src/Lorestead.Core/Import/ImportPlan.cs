using System.Collections.Generic;

namespace Lorestead.Core.Import
{
    public sealed class ImportPlan
    {
        // Parents before children, so the applier can assign positions in one pass.
        public List<ImportedNote> Notes { get; set; }

        // Only attachments that create a new row - a merged note reusing one it
        // already owns is not listed.
        public List<ImportedAttachment> Attachments { get; set; }

        public List<string> Warnings { get; set; }
    }
}
