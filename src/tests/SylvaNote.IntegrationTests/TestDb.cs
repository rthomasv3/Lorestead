using System;
using System.Collections.Generic;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.DataAccess.Migrations;
using SylvaNote.Core.Search;

namespace SylvaNote.IntegrationTests
{
    // One shared-cache in-memory DB per test instance, migrated like a client DB.
    public sealed class TestDb : IDisposable
    {
        public ConnectionManager ConnectionManager { get; }
        public string DeviceId { get; }
        public NoteRepository Notes { get; }
        public BoardRepository Boards { get; }
        public BoardColumnRepository Columns { get; }
        public TaskRepository Tasks { get; }
        public AttachmentRepository Attachments { get; }
        public ChangeLogRepository ChangeLog { get; }
        public SettingsRepository Settings { get; }
        public SyncStateRepository SyncState { get; }
        public SearchRepository Search { get; }

        public TestDb()
            : this(MigrationSets.Client())
        {
        }

        public TestDb(IReadOnlyList<IMigration> migrations)
        {
            ConnectionManager = new ConnectionManager();
            ConnectionManager.OpenInMemory($"testdb-{Guid.NewGuid():N}", migrations);
            DeviceId = Guid.CreateVersion7().ToString();
            Notes = new NoteRepository(ConnectionManager, DeviceId);
            Boards = new BoardRepository(ConnectionManager, DeviceId);
            Columns = new BoardColumnRepository(ConnectionManager, DeviceId);
            Tasks = new TaskRepository(ConnectionManager, DeviceId);
            Attachments = new AttachmentRepository(ConnectionManager, DeviceId);
            ChangeLog = new ChangeLogRepository(ConnectionManager);
            Settings = new SettingsRepository(ConnectionManager);
            SyncState = new SyncStateRepository(ConnectionManager);
            Search = new SearchRepository(ConnectionManager);
        }

        public void Dispose()
        {
            ConnectionManager.Close();
        }
    }
}
