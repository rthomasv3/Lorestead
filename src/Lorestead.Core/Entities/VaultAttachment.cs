using GaldrJson;

namespace Lorestead.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class VaultAttachment
    {
        public string Id { get; set; }
        public string ItemId { get; set; }
        public long SizeBytes { get; set; }
        public bool Deleted { get; set; }
        public int KeyVersion { get; set; }
        public byte[] NameEnc { get; set; }
        public byte[] MimeEnc { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
