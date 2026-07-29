namespace Lorestead.Client.Commands.Contracts;

public sealed class CreateColumnRequest
{
    public string BoardId { get; set; }
    public string Name { get; set; }
}
