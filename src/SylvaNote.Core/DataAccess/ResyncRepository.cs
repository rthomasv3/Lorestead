using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess
{
    // Full-resync wipe (features/sync.md): everything rebuildable from the server
    // goes; pending outbox entries and this device's identity stay. Client-only -
    // it touches client-only tables.
    public sealed class ResyncRepository
    {
        private readonly ConnectionManager _connectionManager;

        public ResyncRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public void WipeSyncedState()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand wipe = connection.CreateCommand();
            // FTS shadow tables empty via the delete triggers on note/task.
            wipe.CommandText = @"
                DELETE FROM note_link;
                DELETE FROM task_note;
                DELETE FROM attachment_thumbnail;
                DELETE FROM attachment_blob;
                DELETE FROM attachment;
                DELETE FROM task;
                DELETE FROM board_column;
                DELETE FROM board;
                DELETE FROM note;
                DELETE FROM change_log WHERE seq IS NOT NULL;
                UPDATE sync_state SET last_seen_seq = 0;";
            wipe.ExecuteNonQuery();
            transaction.Commit();
        }
    }
}
