using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess
{
    public sealed class ServerStateRepository
    {
        private readonly ConnectionManager _connectionManager;

        public ServerStateRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public long GetPrunedThroughSeq()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT pruned_through_seq FROM server_state WHERE id = 1";
            return (long)select.ExecuteScalar();
        }

        public string GetServerId()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT server_id FROM server_state WHERE id = 1";
            return (string)select.ExecuteScalar();
        }

        public long GetLastAssignedSeq()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT last_assigned_seq FROM server_state WHERE id = 1";
            return (long)select.ExecuteScalar();
        }

        // Monotonic allocation: seqs are never reused even when the log tail is
        // deleted, or a cursor at the reused position would silently skip the entry.
        public static long NextSeqWithin(SqliteConnection connection, SqliteTransaction transaction)
        {
            using SqliteCommand allocate = connection.CreateCommand();
            allocate.CommandText = "UPDATE server_state SET last_assigned_seq = last_assigned_seq + 1 WHERE id = 1 RETURNING last_assigned_seq";
            return (long)allocate.ExecuteScalar();
        }

        public static void RaisePrunedThroughSeqWithin(SqliteConnection connection, SqliteTransaction transaction, long seq)
        {
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE server_state SET pruned_through_seq = @seq WHERE id = 1 AND pruned_through_seq < @seq";
            update.Parameters.AddWithValue("@seq", seq);
            update.ExecuteNonQuery();
        }
    }
}
