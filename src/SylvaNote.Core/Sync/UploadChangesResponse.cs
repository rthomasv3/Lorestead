using System.Collections.Generic;
using GaldrJson;

namespace SylvaNote.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class UploadChangesResponse
    {
        // One result per uploaded entry, same order.
        public List<UploadChangeResult> Results { get; set; }
    }
}
