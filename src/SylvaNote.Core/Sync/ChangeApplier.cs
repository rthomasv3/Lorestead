using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.Sync
{
    // Applies pulled server entries in seq order - pure LWW: each upsert overwrites the
    // item row wholesale, so the last entry in the stream wins by construction. One
    // transaction per batch; deferred FKs let mixed-order batches commit.
    public sealed class ChangeApplier
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _localDeviceId;

        public ChangeApplier(ConnectionManager connectionManager, string localDeviceId)
        {
            _connectionManager = connectionManager;
            _localDeviceId = localDeviceId;
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
                    if (entry.DeviceId == _localDeviceId)
                    {
                        // Own change coming back around: item state already reflects it (or a
                        // newer local edit) - stamp/mirror the log only, never reapply.
                        if (!ChangeLogRepository.TryStampPendingWithin(connection, transaction, entry))
                        {
                            ChangeLogRepository.AppendWithin(connection, transaction, entry);
                        }
                    }
                    else if (entry.Op == ChangeOps.Purge)
                    {
                        ApplyPurge(connection, transaction, entry);
                        // Mirrored after the history delete so the purge entry itself survives it.
                        ChangeLogRepository.AppendWithin(connection, transaction, entry);
                    }
                    else
                    {
                        ApplyUpsert(connection, transaction, entry);
                        ChangeLogRepository.AppendWithin(connection, transaction, entry);
                    }
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

        private static void ApplyUpsert(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
        {
            switch (entry.ItemType)
            {
                case ItemTypes.Note:
                    Note note = PayloadJson.Deserialize<Note>(entry.Payload);
                    NoteRepository.UpsertWithin(connection, transaction, note);
                    NoteLinkRebuilder.RebuildForNoteWithin(connection, transaction, note.Id, note.Body);
                    break;
                case ItemTypes.Board:
                    BoardRepository.UpsertWithin(connection, transaction, PayloadJson.Deserialize<Board>(entry.Payload));
                    break;
                case ItemTypes.Column:
                    BoardColumnRepository.UpsertWithin(connection, transaction, PayloadJson.Deserialize<BoardColumn>(entry.Payload));
                    break;
                case ItemTypes.Task:
                    TaskItem task = PayloadJson.Deserialize<TaskItem>(entry.Payload);
                    TaskRepository.UpsertWithin(connection, transaction, task);
                    TaskRepository.ReplaceNoteLinksWithin(connection, transaction, task.Id, task.NoteIds);
                    NoteLinkRebuilder.RebuildForTaskWithin(connection, transaction, task.Id, task.Body);
                    break;
                case ItemTypes.Attachment:
                    AttachmentRepository.UpsertWithin(connection, transaction, PayloadJson.Deserialize<Attachment>(entry.Payload));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown change_log item_type '{entry.ItemType}'.");
            }
        }

        private static void ApplyPurge(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
        {
            switch (entry.ItemType)
            {
                case ItemTypes.Note:
                    NoteRepository.DeleteRowWithin(connection, transaction, entry.ItemId);
                    break;
                case ItemTypes.Board:
                    BoardRepository.DeleteRowWithin(connection, transaction, entry.ItemId);
                    break;
                case ItemTypes.Column:
                    BoardColumnRepository.DeleteRowWithin(connection, transaction, entry.ItemId);
                    break;
                case ItemTypes.Task:
                    TaskRepository.DeleteRowWithin(connection, transaction, entry.ItemId);
                    break;
                case ItemTypes.Attachment:
                    AttachmentRepository.DeleteRowWithin(connection, transaction, entry.ItemId);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown change_log item_type '{entry.ItemType}'.");
            }

            ChangeLogRepository.DeleteForItemWithin(connection, transaction, entry.ItemType, entry.ItemId);
        }
    }
}
