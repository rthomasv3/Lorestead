using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class AddAttachmentResponse
{
    public Attachment Attachment { get; set; }
}
