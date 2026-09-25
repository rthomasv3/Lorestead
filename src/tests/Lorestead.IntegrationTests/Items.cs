using System;
using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.IntegrationTests
{
    public static class Items
    {
        public static string NewId()
        {
            return Guid.CreateVersion7().ToString();
        }

        public static Note Note(string title = "Note", string body = "", string parentId = null)
        {
            return new Note
            {
                Id = NewId(),
                ParentId = parentId,
                Title = title,
                Body = body,
                Position = "V",
            };
        }

        public static Board Board(string name = "Board")
        {
            return new Board
            {
                Id = NewId(),
                Name = name,
                Position = "V",
            };
        }

        public static BoardColumn Column(string boardId, string name = "Column")
        {
            return new BoardColumn
            {
                Id = NewId(),
                BoardId = boardId,
                Name = name,
                Position = "V",
            };
        }

        public static TaskItem Task(string columnId, string title = "Task", string body = "", List<string> noteIds = null, List<string> labels = null)
        {
            return new TaskItem
            {
                Id = NewId(),
                ColumnId = columnId,
                Title = title,
                Body = body,
                Position = "V",
                NoteIds = noteIds ?? new List<string>(),
                Labels = labels ?? new List<string>(),
            };
        }

        public static Vault Vault()
        {
            return new Vault
            {
                Id = NewId(),
                KeyVersion = 1,
            };
        }

        public static VaultKey VaultKey(VaultKeyKind kind, byte[] wrappedKey, byte[] salt = null)
        {
            return new VaultKey
            {
                Id = NewId(),
                Kind = kind,
                WrappedKey = wrappedKey,
                KdfSalt = salt,
                KdfMemoryKiB = salt == null ? null : 256,
                KdfIterations = salt == null ? null : 2,
                KdfParallelism = salt == null ? null : 2,
            };
        }

        public static VaultItem VaultItem(string vaultId, byte[] titleEnc, byte[] bodyEnc, byte[] linksEnc, string parentId = null)
        {
            return new VaultItem
            {
                Id = NewId(),
                VaultId = vaultId,
                ParentId = parentId,
                Position = "V",
                KeyVersion = 1,
                TitleEnc = titleEnc,
                BodyEnc = bodyEnc,
                LinksEnc = linksEnc,
            };
        }

        public static VaultAttachment VaultAttachment(string itemId, byte[] nameEnc, byte[] mimeEnc, long sizeBytes = 3)
        {
            return new VaultAttachment
            {
                Id = NewId(),
                ItemId = itemId,
                SizeBytes = sizeBytes,
                KeyVersion = 1,
                NameEnc = nameEnc,
                MimeEnc = mimeEnc,
            };
        }

        public static Attachment Attachment(string noteId = null, string taskId = null, string filename = "file.png")
        {
            return new Attachment
            {
                Id = NewId(),
                NoteId = noteId,
                TaskId = taskId,
                Filename = filename,
                MimeType = "image/png",
                SizeBytes = 3,
            };
        }
    }
}
