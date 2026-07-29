using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Sync
{
    // Typed wrapper over the server's HTTP surface. The caller owns the HttpClient
    // lifetime; this class owns the wire format and the auth header.
    public sealed class SyncServerClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public SyncServerClient(HttpClient http, string baseUrl, string token)
        {
            _http = http;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<StatusResponse> GetStatus()
        {
            HttpResponseMessage response = await _http.GetAsync($"{_baseUrl}/status");
            response.EnsureSuccessStatusCode();
            return PayloadJson.Deserialize<StatusResponse>(await response.Content.ReadAsStringAsync());
        }

        public async Task<ChangesPageResponse> GetChanges(long since, int limit)
        {
            HttpResponseMessage response = await _http.GetAsync($"{_baseUrl}/changes?since={since}&limit={limit}");

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                throw new ResyncRequiredException();
            }

            response.EnsureSuccessStatusCode();
            return PayloadJson.Deserialize<ChangesPageResponse>(await response.Content.ReadAsStringAsync());
        }

        public async Task<UploadChangesResponse> PostChanges(List<ChangeLogEntry> entries)
        {
            UploadChangesRequest request = new UploadChangesRequest { Entries = entries };
            StringContent body = new StringContent(PayloadJson.Serialize(request), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _http.PostAsync($"{_baseUrl}/changes", body);

            // A 400 carries the server's validation message - surface it (with the
            // status code so callers can classify the failure) or the only trace
            // anywhere is an opaque "400 (Bad Request)".
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new HttpRequestException($"The server rejected the upload: {await response.Content.ReadAsStringAsync()}", null, HttpStatusCode.BadRequest);
            }

            response.EnsureSuccessStatusCode();
            return PayloadJson.Deserialize<UploadChangesResponse>(await response.Content.ReadAsStringAsync());
        }

        // Null when the blob has not been uploaded to the server yet.
        public async Task<byte[]> GetBlob(string attachmentId)
        {
            byte[] data = null;
            HttpResponseMessage response = await _http.GetAsync($"{_baseUrl}/attachments/{attachmentId}/blob");

            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                data = await response.Content.ReadAsByteArrayAsync();
            }

            return data;
        }

        // False when the server has no metadata row yet (blob upload precedes the
        // next drain of a not-yet-synced attachment).
        public async Task<bool> PutBlob(string attachmentId, byte[] data)
        {
            HttpResponseMessage response = await _http.PutAsync($"{_baseUrl}/attachments/{attachmentId}/blob", new ByteArrayContent(data));
            bool saved = response.StatusCode != HttpStatusCode.NotFound;

            if (saved)
            {
                response.EnsureSuccessStatusCode();
            }

            return saved;
        }
    }
}
