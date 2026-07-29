using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Galdr.Native;
using GaldrJson;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;

namespace Lorestead.Client.Services;

// Background sync: one loop runs cycles (startup, WS hints, WS reconnect, manual
// sync, debounced local saves all funnel into the same signal), a second loop
// holds the hint socket open with backoff. Cycles never overlap; failures surface
// only in the Settings sync status and the log - never popups.
public sealed class SyncEngine : ISyncService, IDisposable
{
    private const int LocalChangeDebounceMs = 2000;

    private static readonly GaldrJsonOptions EventOptions =
        new GaldrJsonOptions { PropertyNamingPolicy = PropertyNamingPolicy.CamelCase };

    private readonly ConnectionManager _connectionManager;
    private readonly SettingsRepository _settings;
    private readonly SyncCredentialStore _credentials;
    private readonly IEventService _events;
    private readonly ILoggingService _logger;
    private readonly HttpClient _http = new HttpClient();
    private readonly SemaphoreSlim _cycleLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _syncRequested = new SemaphoreSlim(0);
    private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
    private readonly object _statusLock = new object();
    private readonly Timer _localChangeTimer;
    private ClientWebSocket _socket;
    private volatile int _hintRetryDelaySeconds = 5;
    private bool _started;
    private bool _connected;
    private bool _syncing;
    private string _error;
    private string _lastSyncAt;

    public SyncEngine(
        ConnectionManager connectionManager,
        SettingsRepository settings,
        SyncCredentialStore credentials,
        EventService events,
        ILoggingService logger)
    {
        _connectionManager = connectionManager;
        _settings = settings;
        _credentials = credentials;
        _events = events;
        _logger = logger;
        _localChangeTimer = new Timer(_ => RequestSync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (!_started)
        {
            _started = true;
            Task.Run(() => SyncLoop(_shutdown.Token));
            Task.Run(() => HintLoop(_shutdown.Token));
            RequestSync();
        }
    }

    public void NotifyLocalChange()
    {
        _localChangeTimer.Change(LocalChangeDebounceMs, Timeout.Infinite);
    }

    public GetSyncStatusResponse GetStatus()
    {
        ApplicationSettings settings = _settings.GetApplication();
        bool tokenSet = _credentials.HasToken();

        lock (_statusLock)
        {
            return new GetSyncStatusResponse
            {
                Configured = !string.IsNullOrEmpty(settings.ServerUrl),
                ServerUrl = settings.ServerUrl,
                TokenSet = tokenSet,
                Connected = _connected,
                Syncing = _syncing,
                Error = _error,
                LastSyncAt = _lastSyncAt,
            };
        }
    }

    public async Task<GetSyncStatusResponse> SyncNow()
    {
        return await RunCycle();
    }

    public GetSyncStatusResponse SaveServerUrl(SaveSyncServerUrlRequest request)
    {
        ApplicationSettings settings = _settings.GetApplication();
        settings.ServerUrl = (request.ServerUrl ?? string.Empty).Trim().TrimEnd('/');
        _settings.SaveApplication(settings);
        Reconfigure();
        return GetStatus();
    }

    public GetSyncStatusResponse SaveToken(SaveSyncTokenRequest request)
    {
        try
        {
            _credentials.SaveToken((request.Token ?? string.Empty).Trim());
            Reconfigure();
        }
        catch (Exception ex)
        {
            _logger.Error("Sync", "Storing the sync token failed", ex);
            SetStatus(false, "Storing the sync token failed: " + ex.Message);
        }

        return GetStatus();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _localChangeTimer.Dispose();
        _http.Dispose();
    }

    private void RequestSync()
    {
        _syncRequested.Release();
    }

    // A config change aborts the socket so the hint loop reconnects against the new
    // server, and kicks an immediate cycle so the Settings page gets fast feedback.
    private void Reconfigure()
    {
        _hintRetryDelaySeconds = 1;

        try
        {
            _socket?.Abort();
        }
        catch (ObjectDisposedException)
        {
            // The hint loop can dispose the socket between the null check and the
            // abort - a dead socket already achieves what the abort wanted.
        }

        RequestSync();
    }

    private async Task SyncLoop(CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await _syncRequested.WaitAsync(cancellation);

                // Collapse a burst of triggers into one cycle.
                while (_syncRequested.CurrentCount > 0)
                {
                    await _syncRequested.WaitAsync(cancellation);
                }

                await RunCycle();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<GetSyncStatusResponse> RunCycle()
    {
        await _cycleLock.WaitAsync();

        try
        {
            ApplicationSettings settings = _settings.GetApplication();
            string token = string.IsNullOrEmpty(settings.ServerUrl) ? null : _credentials.GetToken();

            if (string.IsNullOrEmpty(settings.ServerUrl) || string.IsNullOrEmpty(token))
            {
                // Half-configured is a setup state, not a failure - the label names
                // what is missing and no attempt is made.
                SetStatus(false, null);
            }
            else
            {
                SyncState state = new SyncStateRepository(_connectionManager).Get();
                SyncServerClient server = new SyncServerClient(_http, settings.ServerUrl, token);

                lock (_statusLock)
                {
                    _syncing = true;
                }
                PublishStatus();

                // Cheap reachability probe up front: the badge turns green as soon
                // as the server answers, not only after a potentially long first
                // cycle finishes draining.
                await server.GetStatus();

                lock (_statusLock)
                {
                    _connected = true;
                    _error = null;
                }
                PublishStatus();

                SyncCycle cycle = new SyncCycle(_connectionManager, state.DeviceId, server, settings.HistoryRetention);
                SyncCycleResult result = await cycle.Run();

                lock (_statusLock)
                {
                    _lastSyncAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                }

                PublishChangeEvents(result);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Sync", "Sync cycle failed", ex);
            SetStatus(false, DescribeFailure(ex));
        }
        finally
        {
            lock (_statusLock)
            {
                _syncing = false;
            }

            _cycleLock.Release();
        }

        PublishStatus();
        return GetStatus();
    }

    private void PublishChangeEvents(SyncCycleResult result)
    {
        bool attachments = result.ChangedItemTypes.Contains(ItemTypes.Attachment);

        if (attachments || result.ChangedItemTypes.Contains(ItemTypes.Note))
        {
            _events.PublishEvent("notes:changed", "{}");
        }

        // Attachments feed both surfaces: note panels and task card counts.
        if (attachments ||
            result.ChangedItemTypes.Contains(ItemTypes.Board) ||
            result.ChangedItemTypes.Contains(ItemTypes.Column) ||
            result.ChangedItemTypes.Contains(ItemTypes.Task))
        {
            _events.PublishEvent("boards:changed", "{}");
        }
    }

    private void PublishStatus()
    {
        try
        {
            _events.PublishEvent("sync:status", global::GaldrJson.GaldrJson.Serialize(GetStatus(), EventOptions));
        }
        catch (Exception ex)
        {
            _logger.Error("Sync", "Publishing sync status failed", ex);
        }
    }

    // The label is a status surface, not an error dump: raw exception text (which
    // embeds URLs and framework prose) goes to the log; the label gets one of a
    // small fixed set of short states.
    private static string DescribeFailure(Exception ex)
    {
        string text = "Sync failed - check the logs";
        HttpRequestException http = ex as HttpRequestException;

        if (http != null && (http.StatusCode == HttpStatusCode.Unauthorized || http.StatusCode == HttpStatusCode.Forbidden))
        {
            text = "Authentication failed - check the token";
        }
        else if ((http != null && http.StatusCode == null) || ex is TaskCanceledException || ex is SocketException)
        {
            text = "Connection failed";
        }

        return text;
    }

    private void SetStatus(bool connected, string error)
    {
        lock (_statusLock)
        {
            _connected = connected;
            _error = error;
        }
    }

    // The hint socket doubles as the connection monitor: it is the only signal
    // that changes without a sync trigger, so its transitions publish immediately.
    private void SetConnected(bool connected)
    {
        bool changed;

        lock (_statusLock)
        {
            changed = _connected != connected;
            _connected = connected;
        }

        if (changed)
        {
            PublishStatus();
        }
    }

    // Hints are lossy by design: any message (and every reconnect) just triggers the
    // one pull code path, so a missed hint costs nothing.
    private async Task HintLoop(CancellationToken cancellation)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                string url = null;
                string token = null;

                try
                {
                    url = _settings.GetApplication().ServerUrl;
                    token = string.IsNullOrEmpty(url) ? null : _credentials.GetToken();
                }
                catch
                {
                    // DB not open - checked again next round.
                }

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
                }
                else
                {
                    try
                    {
                        using ClientWebSocket socket = new ClientWebSocket();
                        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                        await socket.ConnectAsync(HintUri(url), cancellation);
                        _socket = socket;
                        _hintRetryDelaySeconds = 5;
                        SetConnected(true);
                        RequestSync();

                        while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                        {
                            WebSocketReceiveResult message = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);

                            if (message.MessageType == WebSocketMessageType.Close)
                            {
                                break;
                            }

                            RequestSync();
                        }
                    }
                    catch (Exception) when (!cancellation.IsCancellationRequested)
                    {
                        // Refused or dropped - the backoff below covers both.
                    }

                    _socket = null;
                    SetConnected(false);
                    int delay = _hintRetryDelaySeconds;
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellation);
                    // Capped low because the badge recovers with the reconnect.
                    _hintRetryDelaySeconds = Math.Min(delay * 2, 30);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Uri HintUri(string serverUrl)
    {
        UriBuilder builder = new UriBuilder(serverUrl.TrimEnd('/') + "/ws");
        builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";
        return builder.Uri;
    }
}
