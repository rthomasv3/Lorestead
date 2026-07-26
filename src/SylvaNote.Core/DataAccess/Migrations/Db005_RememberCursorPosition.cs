using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess.Migrations
{
    // Client-only: editor_settings is created by Db002_ClientState. Only the toggle
    // lives here - the remembered offsets themselves are device-local view state in
    // the webview's localStorage, not a column (decisions.md).
    public sealed class Db005_RememberCursorPosition : IMigration
    {
        public int Version => 5;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                ALTER TABLE editor_settings
                ADD COLUMN remember_cursor_position INTEGER NOT NULL DEFAULT 1;
            ";
            command.ExecuteNonQuery();
        }
    }
}
