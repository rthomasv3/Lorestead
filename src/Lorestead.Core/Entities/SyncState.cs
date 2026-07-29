namespace Lorestead.Core.Entities
{
    public sealed class SyncState
    {
        public long LastSeenSeq { get; set; }
        public string DeviceId { get; set; }
    }
}
