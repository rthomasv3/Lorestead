using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetTaskResponse
{
    public TaskItem Task { get; set; }
    public List<Attachment> Attachments { get; set; }
}
