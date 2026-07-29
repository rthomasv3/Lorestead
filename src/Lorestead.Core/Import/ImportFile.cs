namespace Lorestead.Core.Import
{
    public sealed class ImportFile
    {
        // Relative, forward-slashed.
        public string Path { get; set; }

        // Where the bytes really live when Path is synthetic - the Joplin RAW
        // transform renames resource files to their sidecar titles, but the applier
        // still has to read them from the on-disk name. Null means Path is real.
        public string SourcePath { get; set; }

        // Markdown text; null for files the reader did not decode. Binary files are
        // carried by path only - the applier streams their bytes when the plan runs.
        public string Content { get; set; }

        public long SizeBytes { get; set; }
    }
}
