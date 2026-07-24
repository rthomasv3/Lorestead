using System;

namespace SylvaNote.Core.Sync
{
    // GET /changes answered 410: this device's cursor predates a pruned purge entry,
    // so the gap is unreplayable and the client must rebuild from a full pull.
    public sealed class ResyncRequiredException : Exception
    {
        public ResyncRequiredException()
            : base("The server can no longer replay this device's cursor - a full resync is required.")
        {
        }
    }
}
