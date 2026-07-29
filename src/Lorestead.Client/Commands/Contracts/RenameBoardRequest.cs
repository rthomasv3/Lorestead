namespace Lorestead.Client.Commands.Contracts;

public sealed class RenameBoardRequest
{
    public string Id { get; set; }
    public string Name { get; set; }
}
