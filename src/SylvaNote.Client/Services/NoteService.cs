using System;
using System.Collections.Generic;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Ordering;
using SylvaNote.Core.Sync;

namespace SylvaNote.Client.Services;

public sealed class NoteService : INoteService
{
    private readonly RepositoryFactory _repositories;
    private readonly ISyncService _sync;

    public NoteService(RepositoryFactory repositories, ISyncService sync)
    {
        _repositories = repositories;
        _sync = sync;
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
            Backlinks = _repositories.Notes.GetBacklinkSources(request.Id),
        };
    }

    // Every retained version at once, payloads included: the cards show added and
    // removed character counts against the previous version, which the frontend
    // computes with the same diff machinery as the detail view (decisions.md). That
    // needs each version's body and its predecessor's, so a per-card fetch could not
    // build the list at all.
    public GetNoteHistoryResponse GetHistory(GetNoteHistoryRequest request)
    {
        List<NoteVersion> versions = new List<NoteVersion>();
        foreach (ItemVersion version in _repositories.ChangeLog.GetVersionsForItem(ItemTypes.Note, request.NoteId))
        {
            Note payload = PayloadJson.Deserialize<Note>(version.Payload);
            versions.Add(new NoteVersion
            {
                Id = version.Id,
                ChangedAt = version.ChangedAt,
                DeviceId = version.DeviceId,
                SupersededConcurrent = version.SupersededConcurrent,
                Title = payload?.Title ?? string.Empty,
                Body = payload?.Body ?? string.Empty,
            });
        }
        return new GetNoteHistoryResponse { Versions = versions };
    }

    // Title and body only. parent_id, position, type, deleted and created_at carry
    // forward from the current row: reverting those would move the note in the tree,
    // turn a template back into a note, or resurrect a trashed one around the
    // restore-with-parent dialog, none of it visible in a panel showing a body diff
    // (decisions.md). Enforced here rather than in the panel so the rule holds
    // wherever a restore is called from.
    public RestoreNoteVersionResponse RestoreVersion(RestoreNoteVersionRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = notes.Get(request.NoteId);
        if (note == null)
        {
            throw new InvalidOperationException("Note not found.");
        }
        if (note.Deleted)
        {
            throw new InvalidOperationException("Cannot restore a version of a trashed note.");
        }

        ItemVersion version = _repositories.ChangeLog.GetVersionForItem(ItemTypes.Note, request.NoteId, request.VersionId);
        if (version == null)
        {
            throw new InvalidOperationException("That version is no longer in this note's history.");
        }

        Note payload = PayloadJson.Deserialize<Note>(version.Payload);
        note.Title = payload?.Title ?? string.Empty;
        note.Body = payload?.Body ?? string.Empty;
        // One save, so the restore is a single new version - never a rewrite of
        // history, and itself undoable.
        notes.Save(note);
        _sync.NotifyLocalChange();

        return new RestoreNoteVersionResponse
        {
            Title = note.Title,
            Body = note.Body,
            UpdatedAt = note.UpdatedAt,
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
        _sync.NotifyLocalChange();
        return new CreateNoteResponse { Note = note };
    }

    public SaveNoteResponse SaveBody(SaveNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = GetRequired(notes, request.Id);
        note.Body = request.Body ?? string.Empty;
        notes.Save(note);
        _sync.NotifyLocalChange();
        return new SaveNoteResponse { UpdatedAt = note.UpdatedAt };
    }

    public RenameNoteResponse Rename(RenameNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note note = GetRequired(notes, request.Id);
        note.Title = request.Title ?? string.Empty;
        notes.Save(note);
        _sync.NotifyLocalChange();
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
        _sync.NotifyLocalChange();
        return new MoveNoteResponse { Position = note.Position };
    }

    public TrashNoteResponse Trash(TrashNoteRequest request)
    {
        _repositories.Notes.TrashSubtree(request.Id);
        _sync.NotifyLocalChange();
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
        _sync.NotifyLocalChange();
        return new RestoreNoteResponse { Ok = true };
    }

    public RestoreNoteAtResponse RestoreAt(RestoreNoteAtRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        string position = AllocatePosition(notes, request.ParentId, request.PreviousId, request.NextId);
        notes.RestoreSubtreeAt(request.Id, request.ParentId, position);
        _sync.NotifyLocalChange();
        return new RestoreNoteAtResponse { Ok = true };
    }

    public PurgeNoteResponse Purge(PurgeNoteRequest request)
    {
        _repositories.Notes.PurgeSubtree(request.Id);
        _sync.NotifyLocalChange();
        return new PurgeNoteResponse { Ok = true };
    }

    public DuplicateNoteResponse Duplicate(DuplicateNoteRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        Note original = GetRequired(notes, request.Id);
        if (original.Deleted)
        {
            throw new InvalidOperationException("Cannot duplicate a trashed note.");
        }

        string title = string.IsNullOrEmpty(original.Title) ? "Untitled Copy" : original.Title + " Copy";
        // Directly after the original: bounded above by the nearest sibling key, so
        // the copy slots between them. Same collision guard as AllocatePosition.
        string upper = notes.GetNextChildPosition(original.ParentId, original.Position);
        string position = FractionalIndex.Between(original.Position, upper);
        while (notes.ChildPositionExists(original.ParentId, position))
        {
            position = FractionalIndex.Between(position, upper);
        }

        string rootId = notes.DuplicateSubtree(request.Id, title, position);
        _sync.NotifyLocalChange();
        return new DuplicateNoteResponse { RootId = rootId, Title = title };
    }

    public CreateFromTemplateResponse CreateFromTemplate(CreateFromTemplateRequest request)
    {
        NoteRepository notes = _repositories.Notes;
        string position = FractionalIndex.Between(notes.GetMaxChildPosition(request.ParentId), null);
        string rootId = notes.InstantiateTemplate(request.TemplateId, request.Title, request.ParentId, position);
        _sync.NotifyLocalChange();
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
