namespace Lorestead.Client.Commands.Contracts;

public sealed class DownloadAttachmentResponse
{
    // False when the user cancelled the save dialog.
    public bool Saved { get; set; }
}
