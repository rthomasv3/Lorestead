using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.Sync
{
    // Server-side upload path: stamps monotonic seqs onto a batch of pending client
    // entries, flags concurrent overwrites, and applies each payload to the item
    // tables - one transaction per batch, entries kept in the client's edit order so
    // the stream stays LWW by construction.
    public sealed class ChangeIngestor
    {
        private readonly ConnectionManager _connectionManager;
        private readonly int _historyRetention;

        public ChangeIngestor(ConnectionManager connectionManager, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _historyRetention = historyRetention;
        }

        public UploadChangesResponse Ingest(IReadOnlyList<ChangeLogEntry> entries)
        {
            Validate(entries);

            List<UploadChangeResult> results = new List<UploadChangeResult>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            foreach (ChangeLogEntry entry in entries)
            {
                ChangeLogEntry existing = ChangeLogRepository.FindUploadedWithin(connection, transaction, entry);
                if (existing != null)
                {
                    results.Add(new UploadChangeResult
                    {
                        Seq = existing.Seq.Value,
                        SupersededConcurrent = existing.SupersededConcurrent,
                    });
                    continue;
                }

                // A head the uploader didn't base this edit on means it overwrote a
                // concurrent change (including a concurrent create, where base_seq is
                // null but a head already exists).
                long? head = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, entry.ItemType, entry.ItemId);
                entry.Seq = ServerStateRepository.NextSeqWithin(connection, transaction);
                entry.SupersededConcurrent = head != null && entry.BaseSeq != head;

                if (entry.Op == ChangeOps.Purge)
                {
                    PayloadApplier.PurgeWithin(connection, transaction, entry);
                    // Appended after the history delete so the purge entry itself survives it.
                    ChangeLogRepository.AppendWithin(connection, transaction, entry);
                }
                else
                {
                    PayloadApplier.UpsertWithin(connection, transaction, entry);
                    ChangeLogRepository.AppendWithin(connection, transaction, entry);
                    ChangeLogRepository.PruneItemVersionsWithin(connection, transaction, entry.ItemType, entry.ItemId, _historyRetention);
                }

                results.Add(new UploadChangeResult
                {
                    Seq = entry.Seq.Value,
                    SupersededConcurrent = entry.SupersededConcurrent,
                });
            }

            transaction.Commit();

            return new UploadChangesResponse { Results = results };
        }

        private static void Validate(IReadOnlyList<ChangeLogEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentException("Upload contains no entries.");
            }

            for (int i = 0; i < entries.Count; i++)
            {
                ChangeLogEntry entry = entries[i];
                if (entry.Op != ChangeOps.Upsert && entry.Op != ChangeOps.Purge)
                {
                    throw new ArgumentException($"Entry {i}: unknown op '{entry.Op}'.");
                }
                if (entry.ItemType != ItemTypes.Note && entry.ItemType != ItemTypes.Board &&
                    entry.ItemType != ItemTypes.Column && entry.ItemType != ItemTypes.Task &&
                    entry.ItemType != ItemTypes.Attachment)
                {
                    throw new ArgumentException($"Entry {i}: unknown item_type '{entry.ItemType}'.");
                }
                if (string.IsNullOrEmpty(entry.ItemId) || string.IsNullOrEmpty(entry.DeviceId) || string.IsNullOrEmpty(entry.ChangedAt))
                {
                    throw new ArgumentException($"Entry {i}: item_id, device_id, and changed_at are required.");
                }
                if (entry.Op == ChangeOps.Upsert && string.IsNullOrEmpty(entry.Payload))
                {
                    throw new ArgumentException($"Entry {i}: upsert requires a payload.");
                }
            }
        }
    }
}
