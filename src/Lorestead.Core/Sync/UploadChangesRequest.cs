using System.Collections.Generic;
using GaldrJson;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class UploadChangesRequest
    {
        // Pending outbox entries in the client's edit order; device identity rides
        // each entry.
        public List<ChangeLogEntry> Entries { get; set; }
    }
}
