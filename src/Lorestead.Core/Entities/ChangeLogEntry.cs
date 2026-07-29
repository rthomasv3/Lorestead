using GaldrJson;

namespace Lorestead.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class ChangeLogEntry
    {
        public long? Seq { get; set; }
        public string ItemType { get; set; }
        public string ItemId { get; set; }
        public string Op { get; set; }
        public string Payload { get; set; }
        public long? BaseSeq { get; set; }
        public bool SupersededConcurrent { get; set; }
        public string DeviceId { get; set; }
        public string ChangedAt { get; set; }
    }
}
