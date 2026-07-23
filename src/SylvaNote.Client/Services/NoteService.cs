using System;
using System.Collections.Generic;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Ordering;

namespace SylvaNote.Client.Services;

public sealed class NoteService : INoteService
{
    private readonly RepositoryFactory _repositories;

    public NoteService(RepositoryFactory repositories)
    {
        _repositories = repositories;
    }

    public GetNotesResponse GetNotes()
    {
        List<NoteSummary> summaries = new List<NoteSummary>();
        foreach (Note note in _repositories.Notes.GetAll())
        {
            summaries.Add(new NoteSummary
            {
                Id = note.Id,
                ParentId = note.ParentId,
                Title = note.Title,
                Position = note.Position,
                Type = note.Type,
                Deleted = note.Deleted,
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt,
            });
        }
        return new GetNotesResponse { Notes = summaries };
    }

    public GetNoteResponse GetNote(GetNoteRequest request)
    {
        return new GetNoteResponse
        {
            Note = _repositories.Notes.Get(request.Id),
            Attachments = _repositories.Attachments.GetForNote(request.Id),
        };
    }

    public CreateNoteResponse Create(CreateNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = new Note
        {
            Id = Guid.CreateVersion7().ToString(),
            ParentId = request.ParentId,
            Title = request.Title ?? string.Empty,
            Body = string.Empty,
            Position = FractionalIndex.Between(notes.GetMaxChildPosition(request.ParentId), null),
            Type = request.Template ? NoteType.Template : NoteType.Normal,
        };
        notes.Save(note);
        return new CreateNoteResponse { Note = note };
    }

    public SaveNoteResponse SaveBody(SaveNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = GetRequired(notes, request.Id);
        note.Body = request.Body ?? string.Empty;
        notes.Save(note);
        return new SaveNoteResponse { UpdatedAt = note.UpdatedAt };
    }

    public RenameNoteResponse Rename(RenameNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = GetRequired(notes, request.Id);
        note.Title = request.Title ?? string.Empty;
        notes.Save(note);
        return new RenameNoteResponse { UpdatedAt = note.UpdatedAt };
    }

    public MoveNoteResponse Move(MoveNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = GetRequired(notes, request.Id);
        note.ParentId = request.ParentId;
        note.Position = AllocatePosition(notes, request.ParentId, request.PreviousId, request.NextId);
        note.Type = request.Template ? NoteType.Template : NoteType.Normal;
        notes.Save(note);
        return new MoveNoteResponse { Position = note.Position };
    }

    public TrashNoteResponse Trash(TrashNoteRequest request)
    {
        _repositories.Notes.TrashSubtree(request.Id);
        return new TrashNoteResponse { Ok = true };
    }

    public RestoreNoteResponse Restore(RestoreNoteRequest request)
    {
        if (request.WithAncestors)
        {
            _repositories.Notes.RestoreWithAncestors(request.Id);
        }
        else
        {
            _repositories.Notes.RestoreSubtree(request.Id);
        }
        return new RestoreNoteResponse { Ok = true };
    }

    public RestoreNoteAtResponse RestoreAt(RestoreNoteAtRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        string position = AllocatePosition(notes, request.ParentId, request.PreviousId, request.NextId);
        notes.RestoreSubtreeAt(request.Id, request.ParentId, position);
        return new RestoreNoteAtResponse { Ok = true };
    }

    public PurgeNoteResponse Purge(PurgeNoteRequest request)
    {
        _repositories.Notes.PurgeSubtree(request.Id);
        return new PurgeNoteResponse { Ok = true };
    }

    public CreateFromTemplateResponse CreateFromTemplate(CreateFromTemplateRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        string position = FractionalIndex.Between(notes.GetMaxChildPosition(request.ParentId), null);
        string rootId = notes.InstantiateTemplate(request.TemplateId, request.Title, request.ParentId, position);
        return new CreateFromTemplateResponse { RootId = rootId };
    }

    public SearchNotesResponse Search(SearchNotesRequest request)
    {
        return new SearchNotesResponse
        {
            Results = _repositories.Search.SearchNotes(request.Query, request.IncludeTrashed),
        };
    }

    private static Note GetRequired(NoteRepository notes, string id)
    {
        Note note = notes.Get(id);
        if (note == null)
        {
            throw new InvalidOperationException($"Note '{id}' does not exist.");
        }
        return note;
    }

    // Bounds come from the rendered neighbors at the drop location; the uniqueness
    // loop guards against colliding with a hidden (trashed/template) sibling whose
    // key sits between them.
    private static string AllocatePosition(NoteRepository notes, string parentId, string previousId, string nextId)
    {
        string lower = previousId != null ? notes.Get(previousId)?.Position : null;
        string upper = nextId != null ? notes.Get(nextId)?.Position : null;
        if (lower != null && upper != null && string.CompareOrdinal(lower, upper) >= 0)
        {
            // Stale neighbor info from the frontend - fall back to "after previous".
            upper = null;
        }

        string position = FractionalIndex.Between(lower, upper);
        while (notes.ChildPositionExists(parentId, position))
        {
            position = FractionalIndex.Between(position, upper);
        }
        return position;
    }
}
