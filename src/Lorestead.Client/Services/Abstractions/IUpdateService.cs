using System.Threading.Tasks;
using Lorestead.Client.Commands.Contracts;

namespace Lorestead.Client.Services.Abstractions;

public interface IUpdateService
{
    void Start();
    GetUpdateStatusResponse GetStatus();
    Task<GetUpdateStatusResponse> CheckForUpdate();
    Task<GetUpdateStatusResponse> DownloadUpdate();
    GetUpdateStatusResponse ApplyUpdateAndRestart();
}
