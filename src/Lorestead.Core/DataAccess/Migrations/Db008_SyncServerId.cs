using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    // Which server instance this client's sync state belongs to (features/sync.md).
    // Empty means unknown - existing state is presumed to belong to whichever server
    // answers next, so an upgrade in place adopts quietly without a reset.
    public sealed class Db008_SyncServerId : IMigration
    {
        public int Version => 8;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE sync_state ADD COLUMN server_id TEXT NOT NULL DEFAULT ''";
            command.ExecuteNonQuery();
        }
    }
}
