using System.Collections.Generic;

namespace Lorestead.Client.Commands.Contracts;

// The dialog owns the whole task, so save carries full editable state - title,
// body, the linked-note list and the labels - in one write (one outbox entry).
// Labels null means "leave as they are", so a caller that has not loaded them
// cannot wipe them by omission.
public sealed class SaveTaskRequest
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public List<string> NoteIds { get; set; }
    public List<string> Labels { get; set; }
}
