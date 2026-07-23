using System.Collections.Generic;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetNotesResponse
{
    public List<NoteSummary> Notes { get; set; }
}
