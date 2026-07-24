namespace SylvaNote.Core.Sync
{
    public static class SyncProtocol
    {
        // Bumped only on breaking wire changes; /status reports it so clients can
        // refuse to sync against a server they no longer speak.
        public const int Version = 1;
    }
}
