using System.Collections.Generic;
using GaldrJson;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class ChangesPageResponse
    {
        public List<ChangeLogEntry> Entries { get; set; }
        // Overall head of the server stream - the client is caught up once its
        // cursor reaches this (a full page implies another pull either way).
        public long MaxSeq { get; set; }
    }
}
