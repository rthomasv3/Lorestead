namespace Lorestead.Client.Commands.Contracts;

public sealed class RunImportRequest
{
    // Null means the tree root. New notes land under it; merged notes stay put.
    public string DestinationParentId { get; set; }
}
