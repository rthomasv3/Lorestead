using Galdr.Native;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Commands;

internal static class AttachmentCommands
{
    public static GaldrBuilder AddAttachmentCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getAttachments", (GetAttachmentsRequest request, IAttachmentService attachments) => attachments.GetForNote(request));
        builder.AddFunction("addAttachment", (AddAttachmentRequest request, IAttachmentService attachments) => attachments.Add(request));
        builder.AddFunction("renameAttachment", (RenameAttachmentRequest request, IAttachmentService attachments) => attachments.Rename(request));
        builder.AddFunction("deleteAttachment", (DeleteAttachmentRequest request, IAttachmentService attachments) => attachments.Delete(request));
        builder.AddFunction("getAttachmentData", (GetAttachmentDataRequest request, IAttachmentService attachments) => attachments.GetData(request));
        builder.AddFunction("getAttachmentThumbnail", (GetAttachmentThumbnailRequest request, IAttachmentService attachments) => attachments.GetThumbnail(request));
        builder.AddFunction("saveAttachmentThumbnail", (SaveAttachmentThumbnailRequest request, IAttachmentService attachments) => attachments.SaveThumbnail(request));
        builder.AddFunction("downloadAttachment", (DownloadAttachmentRequest request, IAttachmentService attachments) => attachments.Download(request));
        return builder;
    }
}
