using GaldrJson;

namespace Lorestead.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class StatusResponse
    {
        public string AppVersion { get; set; }
        public int ProtocolVersion { get; set; }
        public string ServerId { get; set; }
        public long LastAssignedSeq { get; set; }
    }
}
