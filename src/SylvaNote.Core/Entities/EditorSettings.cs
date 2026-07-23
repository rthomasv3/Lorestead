namespace SylvaNote.Core.Entities
{
    public sealed class EditorSettings
    {
        public int FontSize { get; set; }
        public string FontFamily { get; set; }
        public bool SpellcheckEnabled { get; set; }
        public bool ShowLineCount { get; set; }
        public bool HighlightActiveLine { get; set; }
        public int AutosaveDebounceMs { get; set; }
        public bool MdTables { get; set; }
        public bool MdTaskLists { get; set; }
        public bool MdStrikethrough { get; set; }
        public bool MdAutolinks { get; set; }
        public bool MdFootnotes { get; set; }
        public bool MdCodeHighlighting { get; set; }
        public bool MdHighlight { get; set; }
    }
}
