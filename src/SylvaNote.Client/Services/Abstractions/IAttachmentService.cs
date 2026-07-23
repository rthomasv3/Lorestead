using SylvaNote.Client.Commands.Contracts;

namespace SylvaNote.Client.Services.Abstractions;

public interface IAttachmentService
{
    GetAttachmentsResponse GetForNote(GetAttachmentsRequest request);
    AddAttachmentResponse Add(AddAttachmentRequest request);
    RenameAttachmentResponse Rename(RenameAttachmentRequest request);
    DeleteAttachmentResponse Delete(DeleteAttachmentRequest request);
    GetAttachmentDataResponse GetData(GetAttachmentDataRequest request);
}
