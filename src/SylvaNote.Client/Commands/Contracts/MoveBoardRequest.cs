namespace SylvaNote.Client.Commands.Contracts;

// PreviousId/NextId are the rendered neighbors at the drop location; the backend
// derives the fractional position (same pattern as MoveNoteRequest).
public sealed class MoveBoardRequest
{
    public string Id { get; set; }
    public string PreviousId { get; set; }
    public string NextId { get; set; }
}
