using GaldrJson;

namespace SylvaNote.Core.Sync
{
    [GaldrJsonSerializable]
    public sealed class UploadChangeResult
    {
        public long Seq { get; set; }
        public bool SupersededConcurrent { get; set; }
    }
}
