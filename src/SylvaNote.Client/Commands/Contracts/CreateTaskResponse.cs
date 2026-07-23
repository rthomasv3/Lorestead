using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class CreateTaskResponse
{
    public TaskItem Task { get; set; }
}
