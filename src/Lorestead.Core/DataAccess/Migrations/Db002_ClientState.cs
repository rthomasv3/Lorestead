using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    // Client-only bookkeeping (data.md): the server DB runs the shared migrations without
    // this one. Settings rows are seeded here so reads never face an empty table; defaults
    // not fixed by the spec were chosen pragmatically (see decisions.md, Phase 1 entry).
    public sealed class Db002_ClientState : IMigration
    {
        public int Version => 2;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE sync_state (
                    last_seen_seq INTEGER NOT NULL DEFAULT 0,
                    device_id     TEXT    NOT NULL
                );

                CREATE TABLE application_settings (
                    history_retention    INTEGER NOT NULL DEFAULT 50,
                    server_url           TEXT    NOT NULL DEFAULT '',
                    theme                TEXT    NOT NULL DEFAULT 'system',
                    accent_color         TEXT    NOT NULL DEFAULT '',
                    date_format          TEXT    NOT NULL DEFAULT 'yyyy-MM-dd',
                    time_format          TEXT    NOT NULL DEFAULT 'HH:mm',
                    trash_retention_days INTEGER NOT NULL DEFAULT 30,
                    auto_check_updates   INTEGER NOT NULL DEFAULT 1,
                    auto_update          INTEGER NOT NULL DEFAULT 0,
                    last_update_check_at TEXT    NOT NULL DEFAULT '',
                    new_note_focus       TEXT    NOT NULL DEFAULT 'title',
                    new_task_focus       TEXT    NOT NULL DEFAULT 'title',
                    window_width         INTEGER NOT NULL DEFAULT 1200,
                    window_height        INTEGER NOT NULL DEFAULT 800,
                    window_state         TEXT    NOT NULL DEFAULT 'normal'
                );
                INSERT INTO application_settings DEFAULT VALUES;

                CREATE TABLE editor_settings (
                    font_size            INTEGER NOT NULL DEFAULT 14,
                    font_family          TEXT    NOT NULL DEFAULT '',
                    spellcheck_enabled   INTEGER NOT NULL DEFAULT 1,
                    show_line_count      INTEGER NOT NULL DEFAULT 1,
                    highlight_active_line INTEGER NOT NULL DEFAULT 1,
                    autosave_debounce_ms INTEGER NOT NULL DEFAULT 1000,
                    md_tables            INTEGER NOT NULL DEFAULT 1,
                    md_task_lists        INTEGER NOT NULL DEFAULT 1,
                    md_strikethrough     INTEGER NOT NULL DEFAULT 1,
                    md_autolinks         INTEGER NOT NULL DEFAULT 1,
                    md_footnotes         INTEGER NOT NULL DEFAULT 1,
                    md_code_highlighting INTEGER NOT NULL DEFAULT 1,
                    md_highlight         INTEGER NOT NULL DEFAULT 1
                );
                INSERT INTO editor_settings DEFAULT VALUES;
            ";
            command.ExecuteNonQuery();
        }
    }
}
