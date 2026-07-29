using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class CreateColumnResponse
{
    public BoardColumn Column { get; set; }
}
