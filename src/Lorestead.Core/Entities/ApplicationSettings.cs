namespace Lorestead.Core.Entities
{
    public sealed class ApplicationSettings
    {
        public int HistoryRetention { get; set; }
        public string ServerUrl { get; set; }
        public string Theme { get; set; }
        public string AccentColor { get; set; }
        public string DateFormat { get; set; }
        public string TimeFormat { get; set; }
        public int TrashRetentionDays { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public bool AutoUpdate { get; set; }
        public string LastUpdateCheckAt { get; set; }
        public string NewNoteFocus { get; set; }
        public string NewTaskFocus { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public string WindowState { get; set; }
    }
}
