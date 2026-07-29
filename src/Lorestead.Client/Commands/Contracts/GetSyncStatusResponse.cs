namespace Lorestead.Client.Commands.Contracts;

public sealed class GetSyncStatusResponse
{
    public bool Configured { get; set; }
    public string ServerUrl { get; set; }
    public bool TokenSet { get; set; }
    public bool Connected { get; set; }
    public bool Syncing { get; set; }
    public string Error { get; set; }
    public string LastSyncAt { get; set; }
}
