using GaldrJson;

namespace Lorestead.Core.Entities
{
    /// <summary>
    /// One wrapping of the vault key. The KDF columns are set on password rows only.
    /// </summary>
    [GaldrJsonSerializable]
    public sealed class VaultKey
    {
        public string Id { get; set; }
        public string VaultId { get; set; }
        public VaultKeyKind Kind { get; set; }
        public byte[] WrappedKey { get; set; }
        public byte[] KdfSalt { get; set; }
        public int? KdfMemoryKiB { get; set; }
        public int? KdfIterations { get; set; }
        public int? KdfParallelism { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
