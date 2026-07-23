using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.DataAccess
{
    // Per-device single-row tables (seeded by migration, never empty); settings are not
    // synced and never touch the change log.
    public sealed class SettingsRepository
    {
        private readonly ConnectionManager _connectionManager;

        public SettingsRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public ApplicationSettings GetApplication()
        {
            ApplicationSettings settings = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT history_retention, server_url, theme, accent_color, date_format, time_format,
                       trash_retention_days, auto_check_updates, auto_update, last_update_check_at,
                       new_note_focus, new_task_focus, window_width, window_height, window_state
                FROM application_settings LIMIT 1";
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                settings = new ApplicationSettings
                {
                    HistoryRetention = reader.GetInt32(0),
                    ServerUrl = reader.GetString(1),
                    Theme = reader.GetString(2),
                    AccentColor = reader.GetString(3),
                    DateFormat = reader.GetString(4),
                    TimeFormat = reader.GetString(5),
                    TrashRetentionDays = reader.GetInt32(6),
                    AutoCheckUpdates = reader.GetInt64(7) != 0,
                    AutoUpdate = reader.GetInt64(8) != 0,
                    LastUpdateCheckAt = reader.GetString(9),
                    NewNoteFocus = reader.GetString(10),
                    NewTaskFocus = reader.GetString(11),
                    WindowWidth = reader.GetInt32(12),
                    WindowHeight = reader.GetInt32(13),
                    WindowState = reader.GetString(14),
                };
            }
            return settings;
        }

        public void SaveApplication(ApplicationSettings settings)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = @"
                UPDATE application_settings SET
                    history_retention = @history_retention, server_url = @server_url, theme = @theme,
                    accent_color = @accent_color, date_format = @date_format, time_format = @time_format,
                    trash_retention_days = @trash_retention_days, auto_check_updates = @auto_check_updates,
                    auto_update = @auto_update, last_update_check_at = @last_update_check_at,
                    new_note_focus = @new_note_focus, new_task_focus = @new_task_focus,
                    window_width = @window_width, window_height = @window_height, window_state = @window_state";
            update.Parameters.AddWithValue("@history_retention", settings.HistoryRetention);
            update.Parameters.AddWithValue("@server_url", settings.ServerUrl ?? string.Empty);
            update.Parameters.AddWithValue("@theme", settings.Theme ?? string.Empty);
            update.Parameters.AddWithValue("@accent_color", settings.AccentColor ?? string.Empty);
            update.Parameters.AddWithValue("@date_format", settings.DateFormat ?? string.Empty);
            update.Parameters.AddWithValue("@time_format", settings.TimeFormat ?? string.Empty);
            update.Parameters.AddWithValue("@trash_retention_days", settings.TrashRetentionDays);
            update.Parameters.AddWithValue("@auto_check_updates", settings.AutoCheckUpdates ? 1 : 0);
            update.Parameters.AddWithValue("@auto_update", settings.AutoUpdate ? 1 : 0);
            update.Parameters.AddWithValue("@last_update_check_at", settings.LastUpdateCheckAt ?? string.Empty);
            update.Parameters.AddWithValue("@new_note_focus", settings.NewNoteFocus ?? string.Empty);
            update.Parameters.AddWithValue("@new_task_focus", settings.NewTaskFocus ?? string.Empty);
            update.Parameters.AddWithValue("@window_width", settings.WindowWidth);
            update.Parameters.AddWithValue("@window_height", settings.WindowHeight);
            update.Parameters.AddWithValue("@window_state", settings.WindowState ?? string.Empty);
            update.ExecuteNonQuery();
        }

        public EditorSettings GetEditor()
        {
            EditorSettings settings = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT font_size, font_family, spellcheck_enabled, show_line_count, highlight_active_line,
                       autosave_debounce_ms, md_tables, md_task_lists, md_strikethrough, md_autolinks,
                       md_footnotes, md_code_highlighting, md_highlight
                FROM editor_settings LIMIT 1";
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                settings = new EditorSettings
                {
                    FontSize = reader.GetInt32(0),
                    FontFamily = reader.GetString(1),
                    SpellcheckEnabled = reader.GetInt64(2) != 0,
                    ShowLineCount = reader.GetInt64(3) != 0,
                    HighlightActiveLine = reader.GetInt64(4) != 0,
                    AutosaveDebounceMs = reader.GetInt32(5),
                    MdTables = reader.GetInt64(6) != 0,
                    MdTaskLists = reader.GetInt64(7) != 0,
                    MdStrikethrough = reader.GetInt64(8) != 0,
                    MdAutolinks = reader.GetInt64(9) != 0,
                    MdFootnotes = reader.GetInt64(10) != 0,
                    MdCodeHighlighting = reader.GetInt64(11) != 0,
                    MdHighlight = reader.GetInt64(12) != 0,
                };
            }
            return settings;
        }

        public void SaveEditor(EditorSettings settings)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = @"
                UPDATE editor_settings SET
                    font_size = @font_size, font_family = @font_family, spellcheck_enabled = @spellcheck_enabled,
                    show_line_count = @show_line_count, highlight_active_line = @highlight_active_line,
                    autosave_debounce_ms = @autosave_debounce_ms, md_tables = @md_tables,
                    md_task_lists = @md_task_lists, md_strikethrough = @md_strikethrough,
                    md_autolinks = @md_autolinks, md_footnotes = @md_footnotes,
                    md_code_highlighting = @md_code_highlighting, md_highlight = @md_highlight";
            update.Parameters.AddWithValue("@font_size", settings.FontSize);
            update.Parameters.AddWithValue("@font_family", settings.FontFamily ?? string.Empty);
            update.Parameters.AddWithValue("@spellcheck_enabled", settings.SpellcheckEnabled ? 1 : 0);
            update.Parameters.AddWithValue("@show_line_count", settings.ShowLineCount ? 1 : 0);
            update.Parameters.AddWithValue("@highlight_active_line", settings.HighlightActiveLine ? 1 : 0);
            update.Parameters.AddWithValue("@autosave_debounce_ms", settings.AutosaveDebounceMs);
            update.Parameters.AddWithValue("@md_tables", settings.MdTables ? 1 : 0);
            update.Parameters.AddWithValue("@md_task_lists", settings.MdTaskLists ? 1 : 0);
            update.Parameters.AddWithValue("@md_strikethrough", settings.MdStrikethrough ? 1 : 0);
            update.Parameters.AddWithValue("@md_autolinks", settings.MdAutolinks ? 1 : 0);
            update.Parameters.AddWithValue("@md_footnotes", settings.MdFootnotes ? 1 : 0);
            update.Parameters.AddWithValue("@md_code_highlighting", settings.MdCodeHighlighting ? 1 : 0);
            update.Parameters.AddWithValue("@md_highlight", settings.MdHighlight ? 1 : 0);
            update.ExecuteNonQuery();
        }
    }
}
