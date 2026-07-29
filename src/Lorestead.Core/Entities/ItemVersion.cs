namespace Lorestead.Core.Entities
{
    // A change_log row read for history display rather than for sync. It carries the
    // local row id - a stable key for the version list - which ChangeLogEntry
    // deliberately does not, because that type is the upload wire format and local
    // ids have no meaning to the server.
    public sealed class ItemVersion
    {
        public long Id { get; set; }
        public string ChangedAt { get; set; }
        public string DeviceId { get; set; }
        public bool SupersededConcurrent { get; set; }
        public string Payload { get; set; }
    }
}
