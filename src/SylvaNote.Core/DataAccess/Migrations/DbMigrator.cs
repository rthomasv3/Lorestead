using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess.Migrations
{
    public sealed class DbMigrator
    {
        private readonly List<IMigration> _migrations = new List<IMigration>();

        public DbMigrator Add(IMigration migration)
        {
            _migrations.Add(migration);
            return this;
        }

        public void Run(SqliteConnection connection)
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            EnsureVersionTable(connection);
            int current = GetCurrentVersion(connection);

            _migrations.Sort((a, b) => a.Version.CompareTo(b.Version));
            foreach (IMigration migration in _migrations)
            {
                if (migration.Version > current)
                {
                    using SqliteTransaction transaction = connection.BeginTransaction();
                    migration.Up(connection);

                    using SqliteCommand update = connection.CreateCommand();
                    update.CommandText = "UPDATE schema_version SET version = @version";
                    update.Parameters.AddWithValue("@version", migration.Version);
                    update.ExecuteNonQuery();

                    transaction.Commit();
                }
            }
        }

        private static void EnsureVersionTable(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);" +
                "INSERT INTO schema_version (version) SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM schema_version);";
            command.ExecuteNonQuery();
        }

        private static int GetCurrentVersion(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_version LIMIT 1";
            object result = command.ExecuteScalar();
            return (int)(long)result;
        }
    }
}
