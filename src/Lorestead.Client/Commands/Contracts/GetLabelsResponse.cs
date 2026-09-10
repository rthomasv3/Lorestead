using System.Collections.Generic;

namespace Lorestead.Client.Commands.Contracts;

// Every label in use across all boards, most used first - the task dialog's
// suggestion list. The board header filters from the loaded board instead.
public sealed class GetLabelsResponse
{
    public List<string> Labels { get; set; }
}
