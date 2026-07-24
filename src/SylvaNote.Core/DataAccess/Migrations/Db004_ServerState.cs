using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess.Migrations
{
    // Server-only bookkeeping. pruned_through_seq is the replay watermark: cursors
    // below it predate a pruned purge entry and would resurrect deleted items, so
    // GET /changes answers 410 and the client full-resyncs. last_assigned_seq is the
    // allocation counter - MAX(seq) goes backward when the log tail is deleted
    // (purge cascade, pruning), and a reused seq is invisible to any cursor at or
    // past it.
    public sealed class Db004_ServerState : IMigration
    {
        public int Version => 4;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE server_state (
                    id                 INTEGER PRIMARY KEY CHECK (id = 1),
                    pruned_through_seq INTEGER NOT NULL DEFAULT 0,
                    last_assigned_seq  INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO server_state (id, pruned_through_seq, last_assigned_seq)
                VALUES (1, 0, COALESCE((SELECT MAX(seq) FROM change_log), 0));
            ";
            command.ExecuteNonQuery();
        }
    }
}
