using SylvaNote.Client.Commands.Contracts;

namespace SylvaNote.Client.Services.Abstractions;

public interface INoteService
{
    GetNotesResponse GetNotes();
    GetNoteResponse GetNote(GetNoteRequest request);
    GetNoteHistoryResponse GetHistory(GetNoteHistoryRequest request);
    CreateNoteResponse Create(CreateNoteRequest request);
    SaveNoteResponse SaveBody(SaveNoteRequest request);
    RenameNoteResponse Rename(RenameNoteRequest request);
    MoveNoteResponse Move(MoveNoteRequest request);
    TrashNoteResponse Trash(TrashNoteRequest request);
    RestoreNoteResponse Restore(RestoreNoteRequest request);
    RestoreNoteAtResponse RestoreAt(RestoreNoteAtRequest request);
    PurgeNoteResponse Purge(PurgeNoteRequest request);
    CreateFromTemplateResponse CreateFromTemplate(CreateFromTemplateRequest request);
    SearchNotesResponse Search(SearchNotesRequest request);
}
