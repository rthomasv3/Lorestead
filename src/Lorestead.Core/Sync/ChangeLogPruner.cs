using System;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;

namespace Lorestead.Core.Sync
{
    // Server-side purge-entry aging. Purge entries must outlive their propagation
    // window; once one is pruned, any cursor before its seq can no longer replay
    // safely (it would keep the purged item), so the watermark rises and those
    // clients get 410 -> full resync.
    public sealed class ChangeLogPruner
    {
        private readonly ConnectionManager _connectionManager;

        public ChangeLogPruner(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public void PruneExpiredPurgeEntries(int retentionDays)
        {
            string cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O", CultureInfo.InvariantCulture);

            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            long? maxPruned = ChangeLogRepository.PrunePurgeEntriesBeforeWithin(connection, transaction, cutoff);
            if (maxPruned != null)
            {
                ServerStateRepository.RaisePrunedThroughSeqWithin(connection, transaction, maxPruned.Value);
            }

            transaction.Commit();
        }
    }
}
