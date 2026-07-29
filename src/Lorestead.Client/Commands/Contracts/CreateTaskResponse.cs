using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class CreateTaskResponse
{
    public TaskItem Task { get; set; }
}
