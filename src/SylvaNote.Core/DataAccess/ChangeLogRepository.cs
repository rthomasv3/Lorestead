using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.DataAccess
{
    // All change_log SQL lives here; the *Within statics run inside a caller-owned
    // transaction so item repositories and the ChangeApplier share one write path.
    public sealed class ChangeLogRepository
    {
        private readonly ConnectionManager _connectionManager;

        public ChangeLogRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public static void AppendWithin(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO change_log (seq, item_type, item_id, op, payload, base_seq, superseded_concurrent, device_id, changed_at)
                VALUES (@seq, @item_type, @item_id, @op, @payload, @base_seq, @superseded_concurrent, @device_id, @changed_at)";
            insert.Parameters.AddWithValue("@seq", (object)entry.Seq ?? System.DBNull.Value);
            insert.Parameters.AddWithValue("@item_type", entry.ItemType);
            insert.Parameters.AddWithValue("@item_id", entry.ItemId);
            insert.Parameters.AddWithValue("@op", entry.Op);
            insert.Parameters.AddWithValue("@payload", entry.Payload ?? string.Empty);
            insert.Parameters.AddWithValue("@base_seq", (object)entry.BaseSeq ?? System.DBNull.Value);
            insert.Parameters.AddWithValue("@superseded_concurrent", entry.SupersededConcurrent ? 1 : 0);
            insert.Parameters.AddWithValue("@device_id", entry.DeviceId);
            insert.Parameters.AddWithValue("@changed_at", entry.ChangedAt);
            insert.ExecuteNonQuery();
        }

        public static long? MaxSeqForItemWithin(SqliteConnection connection, SqliteTransaction transaction, string itemType, string itemId)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(seq) FROM change_log WHERE item_type = @item_type AND item_id = @item_id";
            select.Parameters.AddWithValue("@item_type", itemType);
            select.Parameters.AddWithValue("@item_id", itemId);
            object result = select.ExecuteScalar();
            return result is long value ? value : (long?)null;
        }

        public static bool HasSeqWithin(SqliteConnection connection, SqliteTransaction transaction, long seq)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM change_log WHERE seq = @seq LIMIT 1";
            select.Parameters.AddWithValue("@seq", seq);
            return select.ExecuteScalar() != null;
        }

        // Fills the server-stamped seq into the oldest matching pending entry (pull-path
        // recognition of this device's own uploads). False = no pending match.
        public static bool TryStampPendingWithin(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
        {
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = @"
                UPDATE change_log
                SET seq = @seq, superseded_concurrent = @superseded_concurrent
                WHERE id = (
                    SELECT id FROM change_log
                    WHERE seq IS NULL AND device_id = @device_id AND item_type = @item_type
                          AND item_id = @item_id AND changed_at = @changed_at
                    ORDER BY id LIMIT 1)";
            update.Parameters.AddWithValue("@seq", entry.Seq);
            update.Parameters.AddWithValue("@superseded_concurrent", entry.SupersededConcurrent ? 1 : 0);
            update.Parameters.AddWithValue("@device_id", entry.DeviceId);
            update.Parameters.AddWithValue("@item_type", entry.ItemType);
            update.Parameters.AddWithValue("@item_id", entry.ItemId);
            update.Parameters.AddWithValue("@changed_at", entry.ChangedAt);
            return update.ExecuteNonQuery() > 0;
        }

        public static void DeleteForItemWithin(SqliteConnection connection, SqliteTransaction transaction, string itemType, string itemId)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM change_log WHERE item_type = @item_type AND item_id = @item_id";
            delete.Parameters.AddWithValue("@item_type", itemType);
            delete.Parameters.AddWithValue("@item_id", itemId);
            delete.ExecuteNonQuery();
        }

        public List<PendingChange> GetPending()
        {
            List<PendingChange> pending = new List<PendingChange>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT id, seq, item_type, item_id, op, payload, base_seq, superseded_concurrent, device_id, changed_at
                FROM change_log WHERE seq IS NULL ORDER BY id";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                pending.Add(new PendingChange
                {
                    LocalId = reader.GetInt64(0),
                    Entry = ReadEntry(reader),
                });
            }
            return pending;
        }

        public void AssignSeq(long localId, long seq)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE change_log SET seq = @seq WHERE id = @id";
            update.Parameters.AddWithValue("@seq", seq);
            update.Parameters.AddWithValue("@id", localId);
            update.ExecuteNonQuery();
        }

        public List<ChangeLogEntry> GetForItem(string itemType, string itemId)
        {
            List<ChangeLogEntry> entries = new List<ChangeLogEntry>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT id, seq, item_type, item_id, op, payload, base_seq, superseded_concurrent, device_id, changed_at
                FROM change_log WHERE item_type = @item_type AND item_id = @item_id ORDER BY id DESC";
            select.Parameters.AddWithValue("@item_type", itemType);
            select.Parameters.AddWithValue("@item_id", itemId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(ReadEntry(reader));
            }
            return entries;
        }

        public long GetMaxSeq()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(seq) FROM change_log";
            object result = select.ExecuteScalar();
            return result is long value ? value : 0L;
        }

        private static ChangeLogEntry ReadEntry(SqliteDataReader reader)
        {
            return new ChangeLogEntry
            {
                Seq = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                ItemType = reader.GetString(2),
                ItemId = reader.GetString(3),
                Op = reader.GetString(4),
                Payload = reader.GetString(5),
                BaseSeq = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                SupersededConcurrent = reader.GetInt64(7) != 0,
                DeviceId = reader.GetString(8),
                ChangedAt = reader.GetString(9),
            };
        }
    }
}
