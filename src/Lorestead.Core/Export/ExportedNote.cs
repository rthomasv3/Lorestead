namespace Lorestead.Core.Export
{
    public sealed class ExportedNote
    {
        public string Id { get; set; }

        // Relative, forward-slashed, including the .md extension.
        public string Path { get; set; }

        public string Content { get; set; }
    }
}
