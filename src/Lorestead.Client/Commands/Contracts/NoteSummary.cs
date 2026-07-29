using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

// Tree row without the body - the full note loads on selection via getNote.
public sealed class NoteSummary
{
    public string Id { get; set; }
    public string ParentId { get; set; }
    public string Title { get; set; }
    public string Position { get; set; }
    public NoteType Type { get; set; }
    public bool Deleted { get; set; }
    public string CreatedAt { get; set; }
    public string UpdatedAt { get; set; }
}
