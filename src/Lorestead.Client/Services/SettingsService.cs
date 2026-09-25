using System;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;

namespace Lorestead.Client.Services;

public sealed class SettingsService : ISettingsService
{
    // Interface scale bounds in percent. Enforced here as well as by the field: an
    // out-of-range scale (0, say) would leave no usable UI to correct it from.
    private const int MinUiScale = 85;
    private const int MaxUiScale = 130;

    private readonly SettingsRepository _repository;

    public SettingsService(SettingsRepository repository)
    {
        _repository = repository;
    }

    public GetSettingsResponse GetSettings()
    {
        return new GetSettingsResponse
        {
            Application = _repository.GetApplication(),
            Editor = _repository.GetEditor(),
        };
    }

    // Read-modify-write so columns the request doesn't carry (server_url, window state,
    // last_update_check_at) survive an auto-save from the Settings page.
    public GetSettingsResponse SaveApplication(SaveApplicationSettingsRequest request)
    {
        ApplicationSettings settings = _repository.GetApplication();
        settings.HistoryRetention = request.HistoryRetention;
        settings.Theme = request.Theme;
        settings.AccentColor = request.AccentColor;
        settings.DateFormat = request.DateFormat;
        settings.TimeFormat = request.TimeFormat;
        settings.TrashRetentionDays = request.TrashRetentionDays;
        settings.AutoCheckUpdates = request.AutoCheckUpdates;
        settings.AutoUpdate = request.AutoUpdate;
        settings.NewNoteFocus = request.NewNoteFocus;
        settings.NewTaskFocus = request.NewTaskFocus;
        settings.UiScale = Math.Clamp(request.UiScale, MinUiScale, MaxUiScale);
        _repository.SaveApplication(settings);
        return GetSettings();
    }

    public GetSettingsResponse SaveEditor(SaveEditorSettingsRequest request)
    {
        EditorSettings settings = _repository.GetEditor();
        settings.FontSize = request.FontSize;
        settings.FontFamily = request.FontFamily;
        settings.SpellcheckEnabled = request.SpellcheckEnabled;
        settings.ShowLineCount = request.ShowLineCount;
        settings.HighlightActiveLine = request.HighlightActiveLine;
        settings.AutosaveDebounceMs = request.AutosaveDebounceMs;
        settings.RememberCursorPosition = request.RememberCursorPosition;
        settings.WordWrap = request.WordWrap;
        settings.MdTables = request.MdTables;
        settings.MdTaskLists = request.MdTaskLists;
        settings.MdStrikethrough = request.MdStrikethrough;
        settings.MdAutolinks = request.MdAutolinks;
        settings.MdFootnotes = request.MdFootnotes;
        settings.MdCodeHighlighting = request.MdCodeHighlighting;
        settings.MdHighlight = request.MdHighlight;
        _repository.SaveEditor(settings);
        return GetSettings();
    }

    public WindowData GetWindowData()
    {
        ApplicationSettings settings = _repository.GetApplication();
        return new WindowData
        {
            Width = settings.WindowWidth,
            Height = settings.WindowHeight,
            State = settings.WindowState,
        };
    }

    public void SaveWindowSize(int width, int height)
    {
        ApplicationSettings settings = _repository.GetApplication();
        settings.WindowWidth = width;
        settings.WindowHeight = height;
        settings.WindowState = "normal";
        _repository.SaveApplication(settings);
    }

    public void SaveWindowState(string state)
    {
        ApplicationSettings settings = _repository.GetApplication();
        settings.WindowState = state;
        _repository.SaveApplication(settings);
    }
}
