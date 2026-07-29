using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services;

namespace Lorestead.Client.Services.Abstractions;

public interface ISettingsService
{
    GetSettingsResponse GetSettings();
    GetSettingsResponse SaveApplication(SaveApplicationSettingsRequest request);
    GetSettingsResponse SaveEditor(SaveEditorSettingsRequest request);
    WindowData GetWindowData();
    void SaveWindowSize(int width, int height);
    void SaveWindowState(string state);
}
