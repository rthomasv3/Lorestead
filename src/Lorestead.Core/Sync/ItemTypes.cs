using System.Collections.Generic;

namespace Lorestead.Core.Sync
{
    // Wire values for change_log.item_type - fixed protocol strings, not table names.
    public static class ItemTypes
    {
        public const string Note = "note";
        public const string Board = "board";
        public const string Column = "column";
        public const string Task = "task";
        public const string Attachment = "attachment";
        public const string Vault = "vault";
        public const string VaultKey = "vault_key";
        public const string VaultItem = "vault_item";
        public const string VaultAttachment = "vault_attachment";

        private static readonly HashSet<string> Known = new HashSet<string>
        {
            Note, Board, Column, Task, Attachment, Vault, VaultKey, VaultItem, VaultAttachment,
        };

        public static bool IsKnown(string itemType)
        {
            return itemType != null && Known.Contains(itemType);
        }
    }
}
