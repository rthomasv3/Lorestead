namespace SylvaNote.Client.Commands.Contracts;

// Card-level view of a task: body rides along for the snippet render, note links
// don't (the edit dialog loads the full task).
public sealed class TaskSummary
{
    public string Id { get; set; }
    public string ColumnId { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public string Position { get; set; }
    public int AttachmentCount { get; set; }
    public string CreatedAt { get; set; }
    public string UpdatedAt { get; set; }
}
