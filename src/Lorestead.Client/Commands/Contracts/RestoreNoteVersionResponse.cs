namespace Lorestead.Client.Commands.Contracts;

public sealed class RestoreNoteVersionResponse
{
    public string Title { get; set; }
    public string Body { get; set; }
    public string UpdatedAt { get; set; }
}
