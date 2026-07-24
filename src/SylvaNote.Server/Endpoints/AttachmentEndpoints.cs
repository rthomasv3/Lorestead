using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;

namespace SylvaNote.Server.Endpoints;

// Blobs ride these dedicated endpoints as raw bytes - never the change log. The
// metadata row syncs like any item and must exist before its blob transfers.
public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this WebApplication app)
    {
        app.MapGet("/attachments/{id}/blob", (string id, AttachmentRepository attachments) =>
        {
            IResult result;
            Attachment metadata = attachments.Get(id);
            byte[] data = metadata == null ? null : attachments.GetBlob(id);

            if (data == null)
            {
                result = Results.NotFound();
            }
            else
            {
                result = Results.Bytes(data, string.IsNullOrEmpty(metadata.MimeType) ? "application/octet-stream" : metadata.MimeType);
            }

            return result;
        });

        app.MapPut("/attachments/{id}/blob", async (string id, HttpRequest request, AttachmentRepository attachments) =>
        {
            IResult result;

            if (attachments.Get(id) == null)
            {
                result = Results.NotFound();
            }
            else
            {
                using MemoryStream buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer);
                attachments.SaveBlob(id, buffer.ToArray());
                result = Results.NoContent();
            }

            return result;
        });
    }
}
