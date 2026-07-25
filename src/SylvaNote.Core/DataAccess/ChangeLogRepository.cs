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

        public static long MaxSeqWithin(SqliteConnection connection, SqliteTransaction transaction)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(seq) FROM change_log";
            object result = select.ExecuteScalar();
            return result is long value ? value : 0L;
        }

        // Retry recognition for uploads: the same client edit re-POSTed after a lost
        // response is found by its identity tuple instead of being appended twice.
        public static ChangeLogEntry FindUploadedWithin(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT id, seq, item_type, item_id, op, payload, base_seq, superseded_concurrent, device_id, changed_at
                FROM change_log
                WHERE seq IS NOT NULL AND device_id = @device_id AND item_type = @item_type
                      AND item_id = @item_id AND changed_at = @changed_at
                ORDER BY seq LIMIT 1";
            select.Parameters.AddWithValue("@device_id", entry.DeviceId);
            select.Parameters.AddWithValue("@item_type", entry.ItemType);
            select.Parameters.AddWithValue("@item_id", entry.ItemId);
            select.Parameters.AddWithValue("@changed_at", entry.ChangedAt);
            using SqliteDataReader reader = select.ExecuteReader();
            return reader.Read() ? ReadEntry(reader) : null;
        }

        public static bool HasPendingForItemWithin(SqliteConnection connection, SqliteTransaction transaction, string itemType, string itemId)
        {
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM change_log WHERE seq IS NULL AND item_type = @item_type AND item_id = @item_id LIMIT 1";
            select.Parameters.AddWithValue("@item_type", itemType);
            select.Parameters.AddWithValue("@item_id", itemId);
            return select.ExecuteScalar() != null;
        }

        public static List<PendingChange> ReadPendingWithin(SqliteConnection connection, SqliteTransaction transaction)
        {
            List<PendingChange> pending = new List<PendingChange>();
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

        public static void StampWithin(SqliteConnection connection, SqliteTransaction transaction, long localId, long seq, bool supersededConcurrent)
        {
            using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE change_log SET seq = @seq, superseded_concurrent = @superseded_concurrent WHERE id = @id";
            update.Parameters.AddWithValue("@seq", seq);
            update.Parameters.AddWithValue("@superseded_concurrent", supersededConcurrent ? 1 : 0);
            update.Parameters.AddWithValue("@id", localId);
            update.ExecuteNonQuery();
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

        // Per-item history cap, ordered by authored time because LWW already treats
        // changed_at as the ordering authority (decisions.md). Not by seq (a serverless
        // install never stamps one, so nothing was ever eligible and history grew
        // unbounded) and not by id (that is local *arrival* order - a sync pull inserts
        // older remote entries with the highest ids, which would evict newer local
        // versions).
        //
        // The one survivor is the newest pending entry: deleting it drops an edit from
        // the outbox, and a local edit can legitimately sort older than a pulled remote
        // one. Older pending entries are redundant - payloads are full, not deltas, so
        // the newest entry already describes the item. Protecting it can leave keep + 1
        // rows; the cap is approximate by one, deliberately.
        public static void PruneItemVersionsWithin(SqliteConnection connection, SqliteTransaction transaction, string itemType, string itemId, int keep)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = @"
                DELETE FROM change_log
                WHERE id IN (
                    SELECT id FROM change_log
                    WHERE item_type = @item_type AND item_id = @item_id
                    ORDER BY changed_at DESC, id DESC LIMIT -1 OFFSET @keep)
                  AND id IS NOT (
                    SELECT id FROM change_log
                    WHERE item_type = @item_type AND item_id = @item_id AND seq IS NULL
                    ORDER BY changed_at DESC, id DESC LIMIT 1)";
            delete.Parameters.AddWithValue("@item_type", itemType);
            delete.Parameters.AddWithValue("@item_id", itemId);
            delete.Parameters.AddWithValue("@keep", keep);
            delete.ExecuteNonQuery();
        }

        // The local write path: repositories append and cap in one transaction. Without
        // this the cap only ever ran from the sync paths, which a serverless install
        // never reaches.
        public static void AppendAndPruneWithin(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry, int historyRetention)
        {
            AppendWithin(connection, transaction, entry);
            PruneItemVersionsWithin(connection, transaction, entry.ItemType, entry.ItemId, historyRetention);
        }

        // Purge entries have no item history to cap, so they age out by changed_at.
        // Returns the highest pruned seq (the new replay watermark), or null.
        public static long? PrunePurgeEntriesBeforeWithin(SqliteConnection connection, SqliteTransaction transaction, string cutoffChangedAt)
        {
            long? maxPruned;
            using (SqliteCommand select = connection.CreateCommand())
            {
                select.CommandText = "SELECT MAX(seq) FROM change_log WHERE op = 'purge' AND changed_at < @cutoff";
                select.Parameters.AddWithValue("@cutoff", cutoffChangedAt);
                object result = select.ExecuteScalar();
                maxPruned = result is long value ? value : (long?)null;
            }

            if (maxPruned != null)
            {
                using SqliteCommand delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM change_log WHERE op = 'purge' AND changed_at < @cutoff";
                delete.Parameters.AddWithValue("@cutoff", cutoffChangedAt);
                delete.ExecuteNonQuery();
            }

            return maxPruned;
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

        // Post-drain pruning: entries just stamped by the upload response count toward
        // the cap immediately instead of waiting for the next pull to touch the item.
        public void PruneItemVersions(string itemType, string itemId, int keep)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            PruneItemVersionsWithin(connection, null, itemType, itemId, keep);
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

        // Server feed: stamped entries after the client's cursor, oldest first.
        public List<ChangeLogEntry> GetAfter(long since, int limit)
        {
            List<ChangeLogEntry> entries = new List<ChangeLogEntry>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT id, seq, item_type, item_id, op, payload, base_seq, superseded_concurrent, device_id, changed_at
                FROM change_log WHERE seq > @since ORDER BY seq LIMIT @limit";
            select.Parameters.AddWithValue("@since", since);
            select.Parameters.AddWithValue("@limit", limit);
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
