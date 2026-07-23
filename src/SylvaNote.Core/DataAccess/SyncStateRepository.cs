using System;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.DataAccess
{
    public sealed class SyncStateRepository
    {
        private readonly ConnectionManager _connectionManager;

        public SyncStateRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        // Creates this device's identity on first run; UUIDv7 like every other id.
        public SyncState EnsureInitialized()
        {
            return EnsureInitializedWithDevice(Guid.CreateVersion7().ToString());
        }

        public SyncState EnsureInitializedWithDevice(string deviceId)
        {
            SyncState state = Get();
            if (state == null)
            {
                state = new SyncState
                {
                    LastSeenSeq = 0,
                    DeviceId = deviceId,
                };
                using SqliteConnection connection = _connectionManager.CreateConnection();
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO sync_state (last_seen_seq, device_id) VALUES (@seq, @device_id)";
                insert.Parameters.AddWithValue("@seq", state.LastSeenSeq);
                insert.Parameters.AddWithValue("@device_id", state.DeviceId);
                insert.ExecuteNonQuery();
            }
            return state;
        }

        public SyncState Get()
        {
            SyncState state = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT last_seen_seq, device_id FROM sync_state LIMIT 1";
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                state = new SyncState
                {
                    LastSeenSeq = reader.GetInt64(0),
                    DeviceId = reader.GetString(1),
                };
            }
            return state;
        }

        public static void AdvanceLastSeenSeqWithin(SqliteConnection connection, SqliteTransaction transaction, long seq)
        {
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE sync_state SET last_seen_seq = @seq WHERE last_seen_seq < @seq";
            update.Parameters.AddWithValue("@seq", seq);
            update.ExecuteNonQuery();
        }
    }
}
