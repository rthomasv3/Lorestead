using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetAttachmentsResponse
{
    public List<Attachment> Attachments { get; set; }
}
