using GaldrJson;

namespace SylvaNote.Core.Entities
{
    [GaldrJsonSerializable]
    public sealed class Board
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Position { get; set; }
        public bool Deleted { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
