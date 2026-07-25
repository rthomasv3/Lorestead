using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetNoteResponse
{
    public Note Note { get; set; }
    public List<Attachment> Attachments { get; set; }
    public List<NoteBacklink> Backlinks { get; set; }
}
