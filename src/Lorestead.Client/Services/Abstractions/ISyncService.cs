using System.Threading.Tasks;
using Lorestead.Client.Commands.Contracts;

namespace Lorestead.Client.Services.Abstractions;

public interface ISyncService
{
    void Start();
    void Pause();
    void Resume();
    void NotifyLocalChange();
    GetSyncStatusResponse GetStatus();
    Task<GetSyncStatusResponse> SyncNow();
    GetSyncStatusResponse SaveServerUrl(SaveSyncServerUrlRequest request);
    GetSyncStatusResponse SaveToken(SaveSyncTokenRequest request);
}
