using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services;

namespace SylvaNote.Client.Services.Abstractions;

public interface ISettingsService
{
    GetSettingsResponse GetSettings();
    GetSettingsResponse SaveApplication(SaveApplicationSettingsRequest request);
    GetSettingsResponse SaveEditor(SaveEditorSettingsRequest request);
    WindowData GetWindowData();
    void SaveWindowSize(int width, int height);
    void SaveWindowState(string state);
}
