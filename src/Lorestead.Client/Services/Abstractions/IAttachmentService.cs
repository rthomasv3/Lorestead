using Lorestead.Client.Commands.Contracts;

namespace Lorestead.Client.Services.Abstractions;

public interface IAttachmentService
{
    GetAttachmentsResponse GetForNote(GetAttachmentsRequest request);
    AddAttachmentResponse Add(AddAttachmentRequest request);
    RenameAttachmentResponse Rename(RenameAttachmentRequest request);
    DeleteAttachmentResponse Delete(DeleteAttachmentRequest request);
    GetAttachmentDataResponse GetData(GetAttachmentDataRequest request);
    GetAttachmentThumbnailResponse GetThumbnail(GetAttachmentThumbnailRequest request);
    SaveAttachmentThumbnailResponse SaveThumbnail(SaveAttachmentThumbnailRequest request);
    DownloadAttachmentResponse Download(DownloadAttachmentRequest request);
}
