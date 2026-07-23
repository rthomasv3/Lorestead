using GaldrJson;

namespace SylvaNote.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class Note
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Position { get; set; }
        public NoteType Type { get; set; }
        public bool Deleted { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
