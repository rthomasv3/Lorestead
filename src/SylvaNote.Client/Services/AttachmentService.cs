using System;
using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Services;

public sealed class AttachmentService : IAttachmentService
{
    private const long MaxSizeBytes = 100L * 1024 * 1024;

    private readonly RepositoryFactory _repositories;
    private readonly IDialogService _dialogs;
    private readonly ISyncService _sync;

    public AttachmentService(RepositoryFactory repositories, IDialogService dialogs, ISyncService sync)
    {
        _repositories = repositories;
        _dialogs = dialogs;
        _sync = sync;
    }

    public GetAttachmentsResponse GetForNote(GetAttachmentsRequest request)
    {
        return new GetAttachmentsResponse
        {
            Attachments = _repositories.Attachments.GetForNote(request.NoteId),
        };
    }

    public AddAttachmentResponse Add(AddAttachmentRequest request)
    {
        RequireActiveOwner(request.NoteId, request.TaskId);
        byte[] data = Convert.FromBase64String(request.DataBase64 ?? string.Empty);
        if (data.LongLength > MaxSizeBytes)
        {
            throw new InvalidOperationException("Attachment exceeds the 100 MB size limit.");
        }

        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = new Attachment
        {
            Id = Guid.CreateVersion7().ToString(),
            NoteId = request.NoteId,
            TaskId = request.TaskId,
            Filename = string.IsNullOrEmpty(request.Filename) ? "attachment" : request.Filename,
            MimeType = string.IsNullOrEmpty(request.MimeType) ? "application/octet-stream" : request.MimeType,
            SizeBytes = data.LongLength,
        };
        attachments.Save(attachment);
        attachments.SaveBlob(attachment.Id, data);
        if (!string.IsNullOrEmpty(request.ThumbnailBase64))
        {
            attachments.SaveThumbnail(attachment.Id, Convert.FromBase64String(request.ThumbnailBase64));
        }
        _sync.NotifyLocalChange();
        return new AddAttachmentResponse { Attachment = attachment };
    }

    public GetAttachmentThumbnailResponse GetThumbnail(GetAttachmentThumbnailRequest request)
    {
        byte[] data = _repositories.Attachments.GetThumbnail(request.Id);
        return new GetAttachmentThumbnailResponse
        {
            DataBase64 = data != null ? Convert.ToBase64String(data) : string.Empty,
        };
    }

    public SaveAttachmentThumbnailResponse SaveThumbnail(SaveAttachmentThumbnailRequest request)
    {
        _repositories.Attachments.SaveThumbnail(request.Id, Convert.FromBase64String(request.DataBase64 ?? string.Empty));
        return new SaveAttachmentThumbnailResponse { Ok = true };
    }

    // Download never routes the blob through the frontend - bytes go straight from
    // SQLite to the chosen file.
    public DownloadAttachmentResponse Download(DownloadAttachmentRequest request)
    {
        bool saved = false;
        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = GetRequired(attachments, request.Id);
        string path = _dialogs.OpenSaveDialog(defaultName: attachment.Filename);
        if (!string.IsNullOrEmpty(path))
        {
            System.IO.File.WriteAllBytes(path, attachments.GetBlob(request.Id) ?? new byte[0]);
            saved = true;
        }
        return new DownloadAttachmentResponse { Saved = saved };
    }

    public RenameAttachmentResponse Rename(RenameAttachmentRequest request)
    {
        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = GetRequired(attachments, request.Id);
        RequireActiveOwner(attachment.NoteId, attachment.TaskId);
        attachment.Filename = request.Filename ?? string.Empty;
        attachments.Save(attachment);
        _sync.NotifyLocalChange();
        return new RenameAttachmentResponse { Ok = true };
    }

    // Delete tombstones the metadata row; the blob stays until its owner is purged
    // (features/attachments.md lifecycle).
    public DeleteAttachmentResponse Delete(DeleteAttachmentRequest request)
    {
        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = GetRequired(attachments, request.Id);
        RequireActiveOwner(attachment.NoteId, attachment.TaskId);
        attachment.Deleted = true;
        attachments.Save(attachment);
        _sync.NotifyLocalChange();
        return new DeleteAttachmentResponse { Ok = true };
    }

    public GetAttachmentDataResponse GetData(GetAttachmentDataRequest request)
    {
        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = GetRequired(attachments, request.Id);
        byte[] data = attachments.GetBlob(request.Id);
        return new GetAttachmentDataResponse
        {
            Filename = attachment.Filename,
            MimeType = attachment.MimeType,
            DataBase64 = data != null ? Convert.ToBase64String(data) : string.Empty,
        };
    }

    // A trashed note is read-only, and that has to include its attachments. The panel
    // hides the controls; this is the enforcement (the MCP tools guard the same way).
    // Reads stay open - downloading from a trashed note is fine.
    private void RequireActiveOwner(string noteId, string taskId)
    {
        if (!string.IsNullOrEmpty(noteId))
        {
            Note note = _repositories.Notes.Get(noteId);
            if (note == null || note.Deleted)
            {
                throw new InvalidOperationException("Cannot change attachments on a trashed note.");
            }
        }
        else if (!string.IsNullOrEmpty(taskId))
        {
            TaskItem task = _repositories.Tasks.Get(taskId);
            if (task == null || task.Deleted)
            {
                throw new InvalidOperationException("Cannot change attachments on a deleted task.");
            }
        }
    }

    private static Attachment GetRequired(AttachmentRepository attachments, string id)
    {
        Attachment attachment = attachments.Get(id);
        if (attachment == null)
        {
            throw new InvalidOperationException($"Attachment '{id}' does not exist.");
        }
        return attachment;
    }
}
