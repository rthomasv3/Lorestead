using System;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Search;

namespace Lorestead.Client.Services;

// Item repositories need the device id, which only exists after the DB opens -
// so they are built lazily instead of registered at composition time. They also
// carry the history-retention cap, which the user can change in settings at any
// time, so each one is constructed fresh from the current value rather than
// cached; only the device id (a DB read that never changes) is memoized. The
// repositories themselves are two fields and a connection manager - building one
// per call is cheaper than an invalidation protocol between settings and here.
public sealed class RepositoryFactory
{
    private readonly ConnectionManager _connectionManager;
    private SearchRepository _search;
    private ChangeLogRepository _changeLog;
    private string _deviceId;

    public RepositoryFactory(ConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public NoteRepository Notes
    {
        get { return new NoteRepository(_connectionManager, GetDeviceId(), GetHistoryRetention()); }
    }

    public BoardRepository Boards
    {
        get { return new BoardRepository(_connectionManager, GetDeviceId(), GetHistoryRetention()); }
    }

    public BoardColumnRepository Columns
    {
        get { return new BoardColumnRepository(_connectionManager, GetDeviceId(), GetHistoryRetention()); }
    }

    public TaskRepository Tasks
    {
        get { return new TaskRepository(_connectionManager, GetDeviceId(), GetHistoryRetention()); }
    }

    public AttachmentRepository Attachments
    {
        get { return new AttachmentRepository(_connectionManager, GetDeviceId(), GetHistoryRetention()); }
    }

    public SearchRepository Search
    {
        get
        {
            if (_search == null)
            {
                _search = new SearchRepository(_connectionManager);
            }
            return _search;
        }
    }

    // Cached like Search: no device id, no retention - nothing that goes stale.
    public ChangeLogRepository ChangeLog
    {
        get
        {
            if (_changeLog == null)
            {
                _changeLog = new ChangeLogRepository(_connectionManager);
            }
            return _changeLog;
        }
    }

    private string GetDeviceId()
    {
        if (_deviceId == null)
        {
            Core.Entities.SyncState state = new SyncStateRepository(_connectionManager).Get();
            if (state == null)
            {
                throw new InvalidOperationException("Sync state is not initialized - the database failed to open.");
            }
            _deviceId = state.DeviceId;
        }
        return _deviceId;
    }

    private int GetHistoryRetention()
    {
        return new SettingsRepository(_connectionManager).GetApplication().HistoryRetention;
    }
}
