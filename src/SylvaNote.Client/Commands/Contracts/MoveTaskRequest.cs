namespace SylvaNote.Client.Commands.Contracts;

public sealed class MoveTaskRequest
{
    public string Id { get; set; }
    public string ColumnId { get; set; }
    public string PreviousId { get; set; }
    public string NextId { get; set; }
}
