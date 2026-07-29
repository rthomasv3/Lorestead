namespace Lorestead.Client.Commands.Contracts;

// Excludes server_url (Sync Server section, Phase 5), last_update_check_at (backend-managed)
// and the window columns (persisted by OnWindowChanged, never shown in the UI).
public sealed class SaveApplicationSettingsRequest
{
    public int HistoryRetention { get; set; }
    public string Theme { get; set; }
    public string AccentColor { get; set; }
    public string DateFormat { get; set; }
    public string TimeFormat { get; set; }
    public int TrashRetentionDays { get; set; }
    public bool AutoCheckUpdates { get; set; }
    public bool AutoUpdate { get; set; }
    public string NewNoteFocus { get; set; }
    public string NewTaskFocus { get; set; }
}
