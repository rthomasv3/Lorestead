using GaldrJson;

namespace Lorestead.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class Vault
    {
        public string Id { get; set; }
        public int KeyVersion { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
