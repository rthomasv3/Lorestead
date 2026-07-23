using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class AttachmentCommands
{
    public static GaldrBuilder AddAttachmentCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getAttachments", (GetAttachmentsRequest request, IAttachmentService attachments) => attachments.GetForNote(request));
        builder.AddFunction("addAttachment", (AddAttachmentRequest request, IAttachmentService attachments) => attachments.Add(request));
        builder.AddFunction("renameAttachment", (RenameAttachmentRequest request, IAttachmentService attachments) => attachments.Rename(request));
        builder.AddFunction("deleteAttachment", (DeleteAttachmentRequest request, IAttachmentService attachments) => attachments.Delete(request));
        builder.AddFunction("getAttachmentData", (GetAttachmentDataRequest request, IAttachmentService attachments) => attachments.GetData(request));
        return builder;
    }
}
