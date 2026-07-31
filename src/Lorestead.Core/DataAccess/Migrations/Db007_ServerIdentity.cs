using System;
using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    // Server instance identity (features/sync.md): generated with the data and living
    // in it, so the id tracks the data's lineage - a fresh volume gets a fresh id, a
    // backup restore keeps its id. Clients compare it to detect a replaced server and
    // run the adoption reset instead of colliding on a foreign seq space.
    public sealed class Db007_ServerIdentity : IMigration
    {
        public int Version => 7;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                ALTER TABLE server_state ADD COLUMN server_id TEXT NOT NULL DEFAULT '';
                UPDATE server_state SET server_id = @server_id WHERE server_id = '';
            ";
            command.Parameters.AddWithValue("@server_id", Guid.CreateVersion7().ToString());
            command.ExecuteNonQuery();
        }
    }
}
