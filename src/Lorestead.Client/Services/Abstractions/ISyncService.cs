using System.Threading.Tasks;
using Lorestead.Client.Commands.Contracts;

namespace Lorestead.Client.Services.Abstractions;

public interface ISyncService
{
    void Start();
    void Pause();
    void Resume();
    void NotifyLocalChange();

    /// <summary>
    /// The protocol version the configured server last reported, or 0 before any successful cycle.
    /// </summary>
    int ServerProtocolVersion { get; }
    GetSyncStatusResponse GetStatus();
    Task<GetSyncStatusResponse> SyncNow();
    GetSyncStatusResponse SaveServerUrl(SaveSyncServerUrlRequest request);
    GetSyncStatusResponse SaveToken(SaveSyncTokenRequest request);
}
