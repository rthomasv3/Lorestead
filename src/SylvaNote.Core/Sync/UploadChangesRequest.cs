using System.Collections.Generic;
using GaldrJson;
using SylvaNote.Core.Entities;

namespace SylvaNote.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class UploadChangesRequest
    {
        // Pending outbox entries in the client's edit order; device identity rides
        // each entry.
        public List<ChangeLogEntry> Entries { get; set; }
    }
}
