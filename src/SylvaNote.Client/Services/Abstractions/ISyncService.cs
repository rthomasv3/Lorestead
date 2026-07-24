using System.Threading.Tasks;
using SylvaNote.Client.Commands.Contracts;

namespace SylvaNote.Client.Services.Abstractions;

public interface ISyncService
{
    void Start();
    void NotifyLocalChange();
    GetSyncStatusResponse GetStatus();
    Task<GetSyncStatusResponse> SyncNow();
    GetSyncStatusResponse SaveServerUrl(SaveSyncServerUrlRequest request);
    GetSyncStatusResponse SaveToken(SaveSyncTokenRequest request);
}
