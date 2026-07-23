using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Commands.Contracts;

public sealed class GetSettingsResponse
{
    public ApplicationSettings Application { get; set; }
    public EditorSettings Editor { get; set; }
}
