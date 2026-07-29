using System;
using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Sync
{
    // Item-table writes for one change entry - shared by the client-side ChangeApplier
    // (pull) and the server-side ChangeIngestor (upload) so both ends apply payloads
    // identically.
    internal static class PayloadApplier
    {
        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
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

        public static void PurgeWithin(SqliteConnection connection, SqliteTransaction transaction, ChangeLogEntry entry)
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
