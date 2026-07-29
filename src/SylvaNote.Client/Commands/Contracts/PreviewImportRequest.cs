namespace SylvaNote.Client.Commands.Contracts;

public sealed class PreviewImportRequest
{
    // Null means the tree root. The preflight depends on it: the merge check is
    // scoped to the destination's subtree, so changing it changes the plan.
    public string DestinationParentId { get; set; }
}
