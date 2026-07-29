namespace Lorestead.Core.Import
{
    public sealed class ImportedAttachment
    {
        public string Id { get; set; }

        // The first note whose body referenced the file - attachments need an owner.
        public string NoteId { get; set; }

        // Path inside the import set; the applier reads the bytes from here. The blob
        // is not carried in the plan.
        public string SourcePath { get; set; }

        public string Filename { get; set; }

        public string MimeType { get; set; }

        public long SizeBytes { get; set; }
    }
}
