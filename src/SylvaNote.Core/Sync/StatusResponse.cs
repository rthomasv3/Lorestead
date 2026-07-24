using GaldrJson;

namespace SylvaNote.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class StatusResponse
    {
        public string AppVersion { get; set; }
        public int ProtocolVersion { get; set; }
    }
}
