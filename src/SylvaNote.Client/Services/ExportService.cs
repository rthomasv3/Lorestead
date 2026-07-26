using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Export;

namespace SylvaNote.Client.Services;

public sealed class ExportService : IExportService
{
    private const string AllNotesName = "SylvaNote Export";

    // No BOM: both target apps read plain UTF-8, and a BOM shows up as a stray
    // character in front of the first --- for anything that does not strip it.
    private static readonly UTF8Encoding FileEncoding = new UTF8Encoding(false);

    private readonly RepositoryFactory _repositories;
    private readonly IDialogService _dialogs;

    public ExportService(RepositoryFactory repositories, IDialogService dialogs)
    {
        _repositories = repositories;
        _dialogs = dialogs;
    }

    public ExportNotesResponse Export(ExportNotesRequest request)
    {
        List<Note> scoped = Scope(request);
        if (scoped.Count == 0)
        {
            throw new InvalidOperationException("There is nothing to export.");
        }

        ExportLayout layout = MarkdownExportBuilder.Build(new ExportSource
        {
            Notes = scoped,
            Attachments = ScopedAttachments(scoped),
            GroupTemplates = request.Scope == ExportScope.All,
        });

        // A lone note with no attachments is a plain .md. Anything else has to carry a
        // folder structure, so it becomes a zip - and the default filename in the
        // dialog is what makes that switch visible (features/export.md).
        bool single = layout.Notes.Count == 1 && layout.Attachments.Count == 0;
        string baseName = request.Scope == ExportScope.All
            ? AllNotesName
            : ExportFileName.Sanitize(scoped[0].Title);

        string path = _dialogs.OpenSaveDialog(
            filterList: single ? "md" : "zip",
            defaultName: baseName + (single ? ".md" : ".zip"));

        bool saved = false;
        if (!string.IsNullOrEmpty(path))
        {
            if (single)
            {
                File.WriteAllText(path, layout.Notes[0].Content, FileEncoding);
            }
            else
            {
                WriteZip(path, layout);
            }
            saved = true;
        }

        return new ExportNotesResponse
        {
            Saved = saved,
            Path = saved ? path : string.Empty,
            NoteCount = layout.Notes.Count,
        };
    }

    private List<Note> Scope(ExportNotesRequest request)
    {
        List<Note> all = _repositories.Notes.GetAll();
        List<Note> scoped = new List<Note>();

        if (request.Scope == ExportScope.All)
        {
            foreach (Note note in all)
            {
                if (!note.Deleted)
                {
                    scoped.Add(note);
                }
            }
        }
        else
        {
            Note root = null;
            foreach (Note note in all)
            {
                if (string.Equals(note.Id, request.NoteId, StringComparison.OrdinalIgnoreCase))
                {
                    root = note;
                }
            }

            if (root == null || root.Deleted)
            {
                throw new InvalidOperationException("That note is no longer available to export.");
            }

            scoped.Add(root);
            if (request.Scope == ExportScope.Subtree)
            {
                AddDescendants(all, root.Id, scoped);
            }
        }

        return scoped;
    }

    // Breadth-first over the flat list: the tree is small enough that repeated passes
    // beat building an index, and a trashed note prunes its whole branch the way the
    // tree UI shows it.
    private static void AddDescendants(List<Note> all, string parentId, List<Note> scoped)
    {
        HashSet<string> frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { parentId };

        while (frontier.Count > 0)
        {
            HashSet<string> next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Note note in all)
            {
                if (!note.Deleted && !string.IsNullOrEmpty(note.ParentId) && frontier.Contains(note.ParentId))
                {
                    scoped.Add(note);
                    next.Add(note.Id);
                }
            }
            frontier = next;
        }
    }

    private List<Attachment> ScopedAttachments(List<Note> scoped)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Note note in scoped)
        {
            ids.Add(note.Id);
        }

        List<Attachment> attachments = new List<Attachment>();
        foreach (Attachment attachment in _repositories.Attachments.GetAllForNotes())
        {
            if (ids.Contains(attachment.NoteId))
            {
                attachments.Add(attachment);
            }
        }
        return attachments;
    }

    // Blobs go straight from SQLite into the archive entry, the same way attachment
    // download avoids routing bytes through the frontend.
    private void WriteZip(string path, ExportLayout layout)
    {
        AttachmentRepository attachments = _repositories.Attachments;

        using FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create);

        foreach (ExportedNote note in layout.Notes)
        {
            using Stream entry = archive.CreateEntry(note.Path).Open();
            using StreamWriter writer = new StreamWriter(entry, FileEncoding);
            writer.Write(note.Content);
        }

        foreach (ExportedAttachment attachment in layout.Attachments)
        {
            byte[] data = attachments.GetBlob(attachment.Id);
            if (data != null)
            {
                using Stream entry = archive.CreateEntry(attachment.Path).Open();
                entry.Write(data, 0, data.Length);
            }
        }
    }
}
