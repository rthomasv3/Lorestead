namespace SylvaNote.Client.Commands.Contracts;

public sealed class CreateFromTemplateRequest
{
    public string TemplateId { get; set; }
    public string Title { get; set; }
    public string ParentId { get; set; }
}
