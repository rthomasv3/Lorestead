namespace Lorestead.Client.Commands.Contracts;

public sealed class GetUpdateStatusResponse
{
    public bool Supported { get; set; }
    public bool UpdateAvailable { get; set; }
    public string Version { get; set; }
    public bool Downloaded { get; set; }
    public bool Busy { get; set; }
    public string Error { get; set; }
    public string LastCheckedAt { get; set; }
}
