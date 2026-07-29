using GaldrJson;

namespace Lorestead.Core.Sync
{
    // The only message the /ws socket ever carries: "new head is M". Hints are lossy
    // by design - a missed one costs nothing because the next pull catches up.
    [GaldrJsonSerializable]
    public sealed class SyncHint
    {
        public long MaxSeq { get; set; }
    }
}
