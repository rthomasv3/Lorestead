using System;
using System.Threading;
using System.Threading.Tasks;
using Galdr.Native;
using Microsoft.Data.Sqlite;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Sync;

namespace Lorestead.Client.Services;

// Detects the stdio MCP binary's writes: PRAGMA data_version ticks on another
// connection's commits, and the new change_log rows say what changed. Sync-applied
// entries arrive stamped (seq set) and this device's own saves carry its device id,
// so "seq NULL from a foreign device" isolates agent edits exactly.
public sealed class DataVersionWatcher : IChangeWatcher, IDisposable
{
    private const int PollIntervalMs = 1500;

    private readonly ConnectionManager _connectionManager;
    private readonly SyncStateRepository _syncState;
    private readonly ISyncService _sync;
    private readonly IEventService _events;
    private readonly ILoggingService _logger;
    private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
    private readonly object _pauseLock = new object();
    // Non-null while paused; completing it is what resumes the parked loop.
    private TaskCompletionSource<bool> _resumeSignal;
    private bool _started;

    public DataVersionWatcher(
        ConnectionManager connectionManager,
        SyncStateRepository syncState,
        ISyncService sync,
        IEventService events,
        ILoggingService logger)
    {
        _connectionManager = connectionManager;
        _syncState = syncState;
        _sync = sync;
        _events = events;
        _logger = logger;
    }

    public void Start()
    {
        if (!_started)
        {
            _started = true;
            Task.Run(() => WatchLoop(_shutdown.Token));
        }
    }

    // Mobile lifecycle: park the poll loop while the OS has the app suspended.
    // Harmless if called twice or before Start.
    public void Pause()
    {
        lock (_pauseLock)
        {
            if (_resumeSignal == null)
            {
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    // Mobile lifecycle: unpark the poll loop. The next data_version read picks up
    // anything that changed while suspended. Harmless without a matching Pause.
    public void Resume()
    {
        TaskCompletionSource<bool> signal;

        lock (_pauseLock)
        {
            signal = _resumeSignal;
            _resumeSignal = null;
        }

        signal?.TrySetResult(true);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
    }

    // Parks the caller until Resume; the loop calls this at the top of each iteration.
    private async Task WaitWhilePaused(CancellationToken cancellation)
    {
        while (true)
        {
            Task resumeTask;

            lock (_pauseLock)
            {
                resumeTask = _resumeSignal?.Task;
            }

            if (resumeTask == null)
            {
                return;
            }

            await resumeTask.WaitAsync(cancellation);
        }
    }

    private async Task WatchLoop(CancellationToken cancellation)
    {
        try
        {
            while (_connectionManager.ConnectionString == null && !cancellation.IsCancellationRequested)
            {
                // DB failed to open at startup - nothing to watch until a restart fixes it.
                await Task.Delay(PollIntervalMs, cancellation);
            }

            string deviceId = _syncState.Get().DeviceId;

            // data_version is per-connection state - the poll must run on one held-open
            // connection or every read would look like a new version.
            using SqliteConnection connection = _connectionManager.CreateConnection();
            long dataVersion = ReadDataVersion(connection);
            long lastChangeId = ReadMaxChangeId(connection);
            bool faulted = false;

            while (!cancellation.IsCancellationRequested)
            {
                await WaitWhilePaused(cancellation);
                await Task.Delay(PollIntervalMs, cancellation);

                try
                {
                    long current = ReadDataVersion(connection);
                    if (current != dataVersion)
                    {
                        dataVersion = current;
                        lastChangeId = PublishAgentChanges(connection, deviceId, lastChangeId);
                    }
                    faulted = false;
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    if (!faulted)
                    {
                        // Logged once per failure streak - a wedged DB would otherwise
                        // spam the log at poll frequency.
                        _logger.Error("ChangeWatcher", "data_version poll failed", ex);
                        faulted = true;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("ChangeWatcher", "Change watcher stopped", ex);
        }
    }

    private long PublishAgentChanges(SqliteConnection connection, string deviceId, long lastChangeId)
    {
        long maxId = lastChangeId;
        bool notes = false;
        bool boards = false;
        bool agentWrite = false;

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT id, item_type, device_id, seq FROM change_log WHERE id > @last ORDER BY id";
            select.Parameters.AddWithValue("@last", lastChangeId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                maxId = reader.GetInt64(0);
                string itemType = reader.GetString(1);
                string entryDevice = reader.GetString(2);
                bool pending = reader.IsDBNull(3);

                if (pending && entryDevice != deviceId)
                {
                    agentWrite = true;
                    notes = notes || itemType == ItemTypes.Note || itemType == ItemTypes.Attachment;
                    boards = boards || itemType == ItemTypes.Board || itemType == ItemTypes.Column ||
                             itemType == ItemTypes.Task || itemType == ItemTypes.Attachment;
                }
            }
        }

        if (notes)
        {
            _events.PublishEvent("notes:changed", "{}");
        }
        if (boards)
        {
            _events.PublishEvent("boards:changed", "{}");
        }
        if (agentWrite)
        {
            // Agent edits are outbox entries like any local save - kick the sync engine
            // so they upload without waiting for the next user edit.
            _sync.NotifyLocalChange();
        }

        return maxId;
    }

    private static long ReadDataVersion(SqliteConnection connection)
    {
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA data_version";
        return (long)pragma.ExecuteScalar();
    }

    private static long ReadMaxChangeId(SqliteConnection connection)
    {
        using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT MAX(id) FROM change_log";
        object result = select.ExecuteScalar();
        return result is long value ? value : 0L;
    }
}
