using System.Collections.Generic;

namespace Lorestead.Client.Commands.Contracts;

public sealed class RunImportResponse
{
    public int Created { get; set; }

    public int Merged { get; set; }

    public int Skipped { get; set; }

    public int AttachmentCount { get; set; }

    public int TemplateCount { get; set; }

    public List<string> Warnings { get; set; }
}
