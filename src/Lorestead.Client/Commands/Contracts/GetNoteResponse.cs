using System.Collections.Generic;
using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetNoteResponse
{
    public Note Note { get; set; }
    public List<Attachment> Attachments { get; set; }
    public List<NoteBacklink> Backlinks { get; set; }
}
