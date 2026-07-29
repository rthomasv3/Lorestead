using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Import
{
    public sealed class ImportSource
    {
        // Every file in the chosen zip or folder, dot-directories included - the
        // builder does its own filtering so the rules live in one tested place.
        public List<ImportFile> Files { get; set; }

        // Every note in the database, trashed included: merge decisions need type and
        // deleted state, and identical-skip needs title and body.
        public List<Note> ExistingNotes { get; set; }

        // Active note attachments, so a merged note can reuse one it already owns and
        // so attachment:// links already in native form can be verified.
        public List<Attachment> ExistingAttachments { get; set; }

        // Where created roots land; null is the tree root. Merged notes keep their
        // place, and template roots ignore this (they live outside the tree).
        public string DestinationParentId { get; set; }
    }
}
