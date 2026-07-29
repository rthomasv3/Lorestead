namespace Lorestead.Core.Export
{
    public sealed class ExportedAttachment
    {
        public string Id { get; set; }

        // Relative, forward-slashed, always under attachments/. The blob is not
        // carried here - the caller streams it straight from SQLite into the zip.
        public string Path { get; set; }
    }
}
