using System.Collections.Generic;

namespace Lorestead.Core.Export
{
    public sealed class ExportLayout
    {
        public List<ExportedNote> Notes { get; set; }

        public List<ExportedAttachment> Attachments { get; set; }
    }
}
