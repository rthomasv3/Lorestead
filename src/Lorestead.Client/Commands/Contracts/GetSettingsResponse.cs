using Lorestead.Core.Entities;

namespace Lorestead.Client.Commands.Contracts;

public sealed class GetSettingsResponse
{
    public ApplicationSettings Application { get; set; }
    public EditorSettings Editor { get; set; }
}
