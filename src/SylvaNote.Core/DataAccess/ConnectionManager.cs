using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess.Migrations;

namespace SylvaNote.Core.DataAccess
{
    public sealed class ConnectionManager
    {
        private SqliteConnection _inMemoryKeeper;

        public string ConnectionString { get; private set; }

        // password: SQLCipher key, applied as PRAGMA key on every open (server DB);
        // null on the plain-SQLite client. The host picks the matching provider bundle.
        // Returns true when this call created the schema, which is the host's cue to
        // run the first-run seeder (decisions.md).
        public bool Open(string dbPath, IReadOnlyList<IMigration> migrations, string password = null)
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                ForeignKeys = true,
                Pooling = false,
            };
            if (!string.IsNullOrEmpty(password))
            {
                builder.Password = password;
            }
            ConnectionString = builder.ToString();

            using SqliteConnection connection = CreateConnection();

            using (SqliteCommand journal = connection.CreateCommand())
            {
                // WAL is load-bearing: the stdio MCP binary is a second writer process and
                // the client polls PRAGMA data_version for its edits (decisions.md).
                journal.CommandText = "PRAGMA journal_mode=WAL";
                journal.ExecuteNonQuery();
            }

            return Migrate(connection, migrations);
        }

        // Shared-cache named in-memory DB for tests: it lives as long as one connection
        // stays open, so the manager holds a keeper until Close().
        public void OpenInMemory(string name, IReadOnlyList<IMigration> migrations)
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = name,
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                ForeignKeys = true,
                Pooling = false,
            };
            ConnectionString = builder.ToString();

            _inMemoryKeeper = CreateConnection();
            Migrate(_inMemoryKeeper, migrations);
        }

        public SqliteConnection CreateConnection()
        {
            SqliteConnection connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        public void Close()
        {
            if (_inMemoryKeeper != null)
            {
                _inMemoryKeeper.Dispose();
                _inMemoryKeeper = null;
            }

            SqliteConnection.ClearAllPools();
            ConnectionString = null;
        }

        private static bool Migrate(SqliteConnection connection, IReadOnlyList<IMigration> migrations)
        {
            DbMigrator migrator = new DbMigrator();
            foreach (IMigration migration in migrations)
            {
                migrator.Add(migration);
            }
            return migrator.Run(connection);
        }
    }
}
