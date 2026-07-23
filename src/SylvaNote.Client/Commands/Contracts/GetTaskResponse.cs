using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetTaskResponse
{
    public TaskItem Task { get; set; }
    public List<Attachment> Attachments { get; set; }
}
