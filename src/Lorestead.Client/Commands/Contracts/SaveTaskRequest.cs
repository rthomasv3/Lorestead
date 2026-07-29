using System.Collections.Generic;

namespace Lorestead.Client.Commands.Contracts;

// The dialog owns the whole task, so save carries full editable state - title,
// body, and the linked-note list - in one write (one outbox entry).
public sealed class SaveTaskRequest
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public List<string> NoteIds { get; set; }
}
