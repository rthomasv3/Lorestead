#if ANDROID || IOS
using System.Threading.Tasks;
using Galdr.Native;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;

namespace Lorestead.Client.Services;

// Mobile stub: Velopack is a desktop updater (UpdateManager's constructor throws
// PlatformNotSupportedException on Android/iOS), and mobile updates ship through the
// app stores anyway - so the package is desktop-only. Supported=false is the entire
// mobile story, the same signal a desktop dev run (non-Velopack install) sends, so
// the frontend already renders this state.
public sealed class UpdateService : IUpdateService
{
    private readonly SettingsRepository _settings;

    public UpdateService(SettingsRepository settings, IEventService events, ILoggingService logger)
    {
        _settings = settings;
    }

    public void Start()
    {
    }

    public GetUpdateStatusResponse GetStatus()
    {
        return new GetUpdateStatusResponse
        {
            Supported = false,
            LastCheckedAt = _settings.GetApplication().LastUpdateCheckAt,
        };
    }

    public Task<GetUpdateStatusResponse> CheckForUpdate()
    {
        return Task.FromResult(GetStatus());
    }

    public Task<GetUpdateStatusResponse> DownloadUpdate()
    {
        return Task.FromResult(GetStatus());
    }

    public GetUpdateStatusResponse ApplyUpdateAndRestart()
    {
        return GetStatus();
    }
}
#else
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Galdr.Native;
using GaldrJson;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Velopack;
using Velopack.Sources;

namespace Lorestead.Client.Services;

// Velopack updates against the repo's GitHub Releases. Check and download are
// separate steps (auto_update pre-downloads; applying always waits for the user's
// Relaunch to Update). Failures surface in the returned status and the log - never
// popups. Outside a Velopack install (dev runs) everything is a quiet no-op.
public sealed class UpdateService : IUpdateService
{
    private const string LogSource = nameof(UpdateService);
    private const string RepoUrl = "https://github.com/rthomasv3/Lorestead";

    private static readonly GaldrJsonOptions EventOptions =
        new GaldrJsonOptions { PropertyNamingPolicy = PropertyNamingPolicy.CamelCase };

    private readonly SettingsRepository _settings;
    private readonly IEventService _events;
    private readonly ILoggingService _logger;
    private readonly UpdateManager _manager;
    private readonly object _stateLock = new object();
    private UpdateInfo _pendingUpdate;
    private bool _downloaded;
    private bool _busy;
    private string _error;
    private bool _started;

    public UpdateService(SettingsRepository settings, IEventService events, ILoggingService logger)
    {
        _settings = settings;
        _events = events;
        _logger = logger;

        // A build whose own version is a pre-release updates through pre-releases;
        // a stable build only ever sees stable releases.
        bool prerelease = GetOwnVersion().Contains('-');
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease));
    }

    public void Start()
    {
        if (!_started && _manager.IsInstalled)
        {
            _started = true;
            ApplicationSettings settings = _settings.GetApplication();

            if (settings.AutoCheckUpdates)
            {
                Task.Run(async () =>
                {
                    GetUpdateStatusResponse status = await CheckForUpdate();

                    if (status.UpdateAvailable && settings.AutoUpdate)
                    {
                        await DownloadUpdate();
                    }
                });
            }
        }
    }

    public GetUpdateStatusResponse GetStatus()
    {
        string lastCheckedAt = _settings.GetApplication().LastUpdateCheckAt;
        GetUpdateStatusResponse response;

        lock (_stateLock)
        {
            response = new GetUpdateStatusResponse
            {
                Supported = _manager.IsInstalled,
                UpdateAvailable = _pendingUpdate != null,
                Version = _pendingUpdate?.TargetFullRelease.Version.ToString(),
                Downloaded = _downloaded,
                Busy = _busy,
                Error = _error,
                LastCheckedAt = lastCheckedAt,
            };
        }

        return response;
    }

    public async Task<GetUpdateStatusResponse> CheckForUpdate()
    {
        if (_manager.IsInstalled && TryBeginWork())
        {
            try
            {
                UpdateInfo update = await _manager.CheckForUpdatesAsync();

                lock (_stateLock)
                {
                    _pendingUpdate = update;
                    _downloaded = false;
                    _error = null;
                }

                RecordCheckTime();
            }
            catch (Exception ex)
            {
                _logger.Warn(LogSource, $"Update check failed: {ex.Message}");

                lock (_stateLock)
                {
                    _error = "Update check failed";
                }
            }
            finally
            {
                EndWork();
            }
        }

        return PublishStatus();
    }

    public async Task<GetUpdateStatusResponse> DownloadUpdate()
    {
        UpdateInfo pending;
        bool downloaded;

        lock (_stateLock)
        {
            pending = _pendingUpdate;
            downloaded = _downloaded;
        }

        if (pending != null && !downloaded && TryBeginWork())
        {
            try
            {
                await _manager.DownloadUpdatesAsync(pending, PublishProgress);

                lock (_stateLock)
                {
                    _downloaded = true;
                    _error = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(LogSource, $"Update download failed: {ex.Message}");

                lock (_stateLock)
                {
                    _error = "Update download failed";
                }
            }
            finally
            {
                EndWork();
            }
        }

        return PublishStatus();
    }

    public GetUpdateStatusResponse ApplyUpdateAndRestart()
    {
        UpdateInfo pending;
        bool downloaded;

        lock (_stateLock)
        {
            pending = _pendingUpdate;
            downloaded = _downloaded;
        }

        if (pending != null && downloaded)
        {
            string lockError = ProbeMcpLock();

            if (lockError == null)
            {
                _manager.ApplyUpdatesAndRestart(pending);
            }
            else
            {
                lock (_stateLock)
                {
                    _error = lockError;
                }
            }
        }

        return PublishStatus();
    }

    // Windows cannot replace a running exe. If an agent session has the bundled MCP
    // server running, applying the update would fail partway through - probe with an
    // exclusive open so the user can close agent sessions first instead.
    private string ProbeMcpLock()
    {
        string result = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string mcpPath = Path.Combine(AppContext.BaseDirectory, "Lorestead.Mcp.exe");

            if (File.Exists(mcpPath))
            {
                try
                {
                    using FileStream probe = File.Open(mcpPath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException)
                {
                    // A sharing violation here means the exe is running, which is the answer.
                    result = "An agent is using the Lorestead MCP server. Close agent sessions or wait for them to finish, then try again";
                }
            }
        }

        return result;
    }

    private GetUpdateStatusResponse PublishStatus()
    {
        GetUpdateStatusResponse status = GetStatus();
        _events.PublishEvent("update:status", global::GaldrJson.GaldrJson.Serialize(status, EventOptions));
        return status;
    }

    private void PublishProgress(int percent)
    {
        // Hand-rolled payload: the GaldrJson generator only sees command types, and
        // this event's shape is a single int.
        _events.PublishEvent("update:progress", "{\"percent\":" + percent.ToString(CultureInfo.InvariantCulture) + "}");
    }

    private void RecordCheckTime()
    {
        ApplicationSettings settings = _settings.GetApplication();
        settings.LastUpdateCheckAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _settings.SaveApplication(settings);
    }

    private bool TryBeginWork()
    {
        bool result = false;

        lock (_stateLock)
        {
            if (!_busy)
            {
                _busy = true;
                result = true;
            }
        }

        return result;
    }

    private void EndWork()
    {
        lock (_stateLock)
        {
            _busy = false;
        }
    }

    private static string GetOwnVersion()
    {
        // Auto-stamped by the build (MinVer); never manually synced.
        AssemblyInformationalVersionAttribute attribute =
            typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute == null ? "dev" : attribute.InformationalVersion;
    }
}
#endif
