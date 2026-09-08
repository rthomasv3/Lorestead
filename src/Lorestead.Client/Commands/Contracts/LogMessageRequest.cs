namespace Lorestead.Client.Commands.Contracts;

public sealed class LogMessageRequest
{
    public string Source { get; set; }
    public string Message { get; set; }
}
