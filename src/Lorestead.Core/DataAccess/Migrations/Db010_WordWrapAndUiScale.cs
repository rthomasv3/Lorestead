using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    // Client-only: both settings tables are created by Db002_ClientState. Word wrap
    // defaults on, which is how the editor always behaved. The interface scale is a
    // whole-number percent of the webview's default root font size (100 = unscaled).
    public sealed class Db010_WordWrapAndUiScale : IMigration
    {
        public int Version => 10;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                ALTER TABLE editor_settings
                ADD COLUMN word_wrap INTEGER NOT NULL DEFAULT 1;

                ALTER TABLE application_settings
                ADD COLUMN ui_scale INTEGER NOT NULL DEFAULT 100;
            ";
            command.ExecuteNonQuery();
        }
    }
}
