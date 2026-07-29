namespace Lorestead.Core.Entities
{
    // Pairs an outbox entry with its local rowid so an assigned seq can be written back
    // after upload; the rowid never leaves the process (not serialized).
    public sealed class PendingChange
    {
        public long LocalId { get; set; }
        public ChangeLogEntry Entry { get; set; }
    }
}
