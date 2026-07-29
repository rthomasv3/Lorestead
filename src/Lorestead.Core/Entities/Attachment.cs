using GaldrJson;

namespace Lorestead.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class Attachment
    {
        public string Id { get; set; }
        public string NoteId { get; set; }
        public string TaskId { get; set; }
        public string Filename { get; set; }
        public string MimeType { get; set; }
        public long SizeBytes { get; set; }
        public bool Deleted { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
