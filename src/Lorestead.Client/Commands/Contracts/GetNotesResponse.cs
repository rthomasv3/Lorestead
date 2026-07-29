using System.Collections.Generic;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetNotesResponse
{
    public List<NoteSummary> Notes { get; set; }
}
