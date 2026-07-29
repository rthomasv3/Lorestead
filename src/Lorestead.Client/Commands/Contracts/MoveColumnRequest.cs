namespace Lorestead.Client.Commands.Contracts;

public sealed class MoveColumnRequest
{
    public string Id { get; set; }
    public string PreviousId { get; set; }
    public string NextId { get; set; }
}
