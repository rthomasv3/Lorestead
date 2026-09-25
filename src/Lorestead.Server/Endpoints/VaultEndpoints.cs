using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Lorestead.Core.DataAccess;

namespace Lorestead.Server.Endpoints;

/// <summary>
/// Vault attachment blobs: ciphertext in, ciphertext out, always octet-stream because the mime type is encrypted.
/// </summary>
public static class VaultEndpoints
{
    public static void MapVaultEndpoints(this WebApplication app)
    {
        app.MapGet("/vault/attachments/{id}/blob", (string id, VaultAttachmentRepository attachments) =>
        {
            IResult result;
            byte[] data = attachments.Get(id) == null ? null : attachments.GetBlob(id);

            if (data == null)
            {
                result = Results.NotFound();
            }
            else
            {
                result = Results.Bytes(data, "application/octet-stream");
            }

            return result;
        });

        app.MapPut("/vault/attachments/{id}/blob", async (string id, HttpRequest request, VaultAttachmentRepository attachments) =>
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
