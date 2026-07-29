namespace Lorestead.Client.Commands.Contracts;

public sealed class CreateTaskRequest
{
    public string ColumnId { get; set; }
    public string Title { get; set; }
}
