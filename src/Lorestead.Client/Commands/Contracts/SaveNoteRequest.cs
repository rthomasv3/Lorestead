namespace Lorestead.Client.Commands.Contracts;

// Body only - titles change through renameNote (tree rename / first-line auto-fill).
public sealed class SaveNoteRequest
{
    public string Id { get; set; }
    public string Body { get; set; }
}
