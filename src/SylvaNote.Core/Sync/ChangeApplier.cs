using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.Sync
{
    // Applies pulled server entries in seq order - pure LWW: the item row always holds
    // the newest known state, meaning the highest stamped seq for the item or a pending
    // local edit that will outrank it once uploaded (see LocalStateIsAhead). One
    // transaction per batch; deferred FKs let mixed-order batches commit.
    public sealed class ChangeApplier
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _localDeviceId;
        private readonly int _historyRetention;

        public ChangeApplier(ConnectionManager connectionManager, string localDeviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _localDeviceId = localDeviceId;
            _historyRetention = historyRetention;
        }

        public void Apply(IReadOnlyList<ChangeLogEntry> entries)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            long maxSeq = 0;
            foreach (ChangeLogEntry entry in entries.OrderBy(e => e.Seq))
            {
                if (entry.Seq == null)
                {
                    throw new InvalidOperationException("Server entries must carry a seq.");
                }

                if (!ChangeLogRepository.HasSeqWithin(connection, transaction, entry.Seq.Value))
                {
                    // Own change whose pending entry is still here: stamping is the whole
                    // job - the item row already reflects it (or a newer local edit). An own
                    // entry with no pending match (post-resync wipe) flows through the normal
                    // apply path, or the wiped row would never get its own state back.
                    bool stampedOwnPending = entry.DeviceId == _localDeviceId
                        && ChangeLogRepository.TryStampPendingWithin(connection, transaction, entry);

                    if (!stampedOwnPending)
                    {
                        if (LocalStateIsAhead(connection, transaction, entry))
                        {
                            // The item row must keep the newest known state: a pending outbox edit
                            // outranks this entry once uploaded, and an already stamped higher seq
                            // (outbox drain racing the pull) makes it old news. Mirror only -
                            // applying it would show the losing version on the winning device.
                            // Purges follow the same rule: a concurrent local edit resurrects the
                            // item on the server, so deleting here would diverge (decisions.md).
                            ChangeLogRepository.AppendWithin(connection, transaction, entry);
                        }
                        else if (entry.Op == ChangeOps.Purge)
                        {
                            PayloadApplier.PurgeWithin(connection, transaction, entry);
                            // Mirrored after the history delete so the purge entry itself survives it.
                            ChangeLogRepository.AppendWithin(connection, transaction, entry);
                        }
                        else
                        {
                            PayloadApplier.UpsertWithin(connection, transaction, entry);
                            ChangeLogRepository.AppendWithin(connection, transaction, entry);
                        }
                    }

                    ChangeLogRepository.PruneItemVersionsWithin(connection, transaction, entry.ItemType, entry.ItemId, _historyRetention);
                }

                if (entry.Seq.Value > maxSeq)
                {
                    maxSeq = entry.Seq.Value;
                }
            }

            if (maxSeq > 0)
            {
                SyncStateRepository.AdvanceLastSeenSeqWithin(connection, transaction, maxSeq);
            }

            transaction.Commit();
        }

        private static bool LocalStateIsAhead(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
        {
            bool ahead = ChangeLogRepository.HasPendingForItemWithin(connection, transaction, entry.ItemType, entry.ItemId);

            if (!ahead)
            {
                long? head = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, entry.ItemType, entry.ItemId);
                ahead = head != null && head.Value >= entry.Seq.Value;
            }

            return ahead;
        }
    }
}
