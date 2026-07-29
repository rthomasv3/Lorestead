using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Sync
{
    // Server-side MCP writes ride the same repository save path a client uses, which
    // appends seq-NULL outbox entries; this promotes them to stamped server entries so
    // they enter the /changes feed. Mirrors ChangeIngestor's stamping semantics.
    public sealed class ServerChangeStamper
    {
        private readonly ConnectionManager _connectionManager;
        private readonly int _historyRetention;

        public ServerChangeStamper(ConnectionManager connectionManager, int historyRetention)
        {
            _connectionManager = connectionManager;
            _historyRetention = historyRetention;
        }

        // Returns the log's max seq for the hint broadcast.
        public long StampPending()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            foreach (PendingChange change in ChangeLogRepository.ReadPendingWithin(connection, transaction))
            {
                ChangeLogEntry entry = change.Entry;
                long? head = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, entry.ItemType, entry.ItemId);
                entry.Seq = ServerStateRepository.NextSeqWithin(connection, transaction);
                entry.SupersededConcurrent = head != null && entry.BaseSeq != head;

                if (entry.Op == ChangeOps.Purge)
                {
                    // PurgeWithin deletes the item's whole history including this row,
                    // so the stamped entry is re-appended after it (ChangeIngestor order).
                    PayloadApplier.PurgeWithin(connection, transaction, entry);
                    ChangeLogRepository.AppendWithin(connection, transaction, entry);
                }
                else
                {
                    ChangeLogRepository.StampWithin(connection, transaction, change.LocalId, entry.Seq.Value, entry.SupersededConcurrent);
                    // Re-applied even though the tool write already updated state: a client
                    // upload ingested between that write and this stamp would otherwise
                    // leave state disagreeing with seq order.
                    PayloadApplier.UpsertWithin(connection, transaction, entry);
                    ChangeLogRepository.PruneItemVersionsWithin(connection, transaction, entry.ItemType, entry.ItemId, _historyRetention);
                }
            }

            long maxSeq = ChangeLogRepository.MaxSeqWithin(connection, transaction);
            transaction.Commit();
            return maxSeq;
        }
    }
}
