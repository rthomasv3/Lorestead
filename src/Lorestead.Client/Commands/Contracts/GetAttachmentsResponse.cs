using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetAttachmentsResponse
{
    public List<Attachment> Attachments { get; set; }
}
