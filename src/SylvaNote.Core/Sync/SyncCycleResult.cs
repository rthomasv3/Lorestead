using System.Collections.Generic;

namespace SylvaNote.Core.Sync
{
    public sealed class SyncCycleResult
    {
        public int Uploaded { get; set; }
        public int Applied { get; set; }
        public int BlobsUploaded { get; set; }
        public int BlobsDownloaded { get; set; }
        public bool Resynced { get; set; }
        // Item types seen in pulled entries - the client maps these to store refresh
        // events (notes:changed, boards:changed, ...).
        public HashSet<string> ChangedItemTypes { get; } = new HashSet<string>();
    }
}
