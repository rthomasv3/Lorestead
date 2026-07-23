using System;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;

namespace SylvaNote.Client.Services;

public sealed class AttachmentService : IAttachmentService
{
    private const long MaxSizeBytes = 100L * 1024 * 1024;

    private readonly RepositoryFactory _repositories;

    public AttachmentService(RepositoryFactory repositories)
    {
        _repositories = repositories;
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
            Filename = string.IsNullOrEmpty(request.Filename) ? "attachment" : request.Filename,
            MimeType = string.IsNullOrEmpty(request.MimeType) ? "application/octet-stream" : request.MimeType,
            SizeBytes = data.LongLength,
        };
        attachments.Save(attachment);
        attachments.SaveBlob(attachment.Id, data);
        return new AddAttachmentResponse { Attachment = attachment };
    }

    public RenameAttachmentResponse Rename(RenameAttachmentRequest request)
    {
        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = GetRequired(attachments, request.Id);
        attachment.Filename = request.Filename ?? string.Empty;
        attachments.Save(attachment);
        return new RenameAttachmentResponse { Ok = true };
    }

    // Delete tombstones the metadata row; the blob stays until its owner is purged
    // (features/attachments.md lifecycle).
    public DeleteAttachmentResponse Delete(DeleteAttachmentRequest request)
    {
        AttachmentRepository attachments = _repositories.Attachments;
        Attachment attachment = GetRequired(attachments, request.Id);
        attachment.Deleted = true;
        attachments.Save(attachment);
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
