using System;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Search;

namespace SylvaNote.Client.Services;

// Item repositories need the device id, which only exists after the DB opens —
// so they are built lazily instead of registered at composition time.
public sealed class RepositoryFactory
{
    private readonly ConnectionManager _connectionManager;
    private NoteRepository _notes;
    private AttachmentRepository _attachments;
    private SearchRepository _search;

    public RepositoryFactory(ConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public NoteRepository Notes
    {
        get
        {
            if (_notes == null)
            {
                _notes = new NoteRepository(_connectionManager, GetDeviceId());
            }
            return _notes;
        }
    }

    public AttachmentRepository Attachments
    {
        get
        {
            if (_attachments == null)
            {
                _attachments = new AttachmentRepository(_connectionManager, GetDeviceId());
            }
            return _attachments;
        }
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

    private string GetDeviceId()
    {
        Core.Entities.SyncState state = new SyncStateRepository(_connectionManager).Get();
        if (state == null)
        {
            throw new InvalidOperationException("Sync state is not initialized — the database failed to open.");
        }
        return state.DeviceId;
    }
}
