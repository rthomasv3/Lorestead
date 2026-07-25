using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class NoteCommands
{
    public static GaldrBuilder AddNoteCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getNotes", (INoteService notes) => notes.GetNotes());
        builder.AddFunction("getNote", (GetNoteRequest request, INoteService notes) => notes.GetNote(request));
        builder.AddFunction("getNoteHistory", (GetNoteHistoryRequest request, INoteService notes) => notes.GetHistory(request));
        builder.AddFunction("restoreNoteVersion", (RestoreNoteVersionRequest request, INoteService notes) => notes.RestoreVersion(request));
        builder.AddFunction("createNote", (CreateNoteRequest request, INoteService notes) => notes.Create(request));
        builder.AddFunction("saveNote", (SaveNoteRequest request, INoteService notes) => notes.SaveBody(request));
        builder.AddFunction("renameNote", (RenameNoteRequest request, INoteService notes) => notes.Rename(request));
        builder.AddFunction("moveNote", (MoveNoteRequest request, INoteService notes) => notes.Move(request));
        builder.AddFunction("trashNote", (TrashNoteRequest request, INoteService notes) => notes.Trash(request));
        builder.AddFunction("restoreNote", (RestoreNoteRequest request, INoteService notes) => notes.Restore(request));
        builder.AddFunction("restoreNoteAt", (RestoreNoteAtRequest request, INoteService notes) => notes.RestoreAt(request));
        builder.AddFunction("purgeNote", (PurgeNoteRequest request, INoteService notes) => notes.Purge(request));
        builder.AddFunction("createFromTemplate", (CreateFromTemplateRequest request, INoteService notes) => notes.CreateFromTemplate(request));
        builder.AddFunction("searchNotes", (SearchNotesRequest request, INoteService notes) => notes.Search(request));
        return builder;
    }
}
