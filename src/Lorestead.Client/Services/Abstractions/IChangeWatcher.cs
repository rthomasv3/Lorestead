namespace Lorestead.Client.Services.Abstractions;

public interface IChangeWatcher
{
    void Start();
    void Pause();
    void Resume();
}
