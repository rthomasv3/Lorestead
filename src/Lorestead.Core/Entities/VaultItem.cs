using GaldrJson;

namespace Lorestead.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class VaultItem
    {
        public string Id { get; set; }
        public string VaultId { get; set; }
        public string ParentId { get; set; }
        public string Position { get; set; }
        public bool Deleted { get; set; }
        public int KeyVersion { get; set; }
        public byte[] TitleEnc { get; set; }
        public byte[] BodyEnc { get; set; }
        public byte[] LinksEnc { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
