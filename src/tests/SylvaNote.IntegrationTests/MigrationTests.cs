using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess.Migrations;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class MigrationTests
    {
        [Fact]
        public void ClientMigrationsBuildFullSchemaFromScratch()
        {
            using TestDb db = new TestDb();
            List<string> names = GetSchemaNames(db, "table");
            Assert.Contains("note", names);
            Assert.Contains("board", names);
            Assert.Contains("board_column", names);
            Assert.Contains("task", names);
            Assert.Contains("task_note", names);
            Assert.Contains("attachment", names);
            Assert.Contains("attachment_blob", names);
            Assert.Contains("attachment_thumbnail", names);
            Assert.Contains("change_log", names);
            Assert.Contains("note_link", names);
            Assert.Contains("note_fts", names);
            Assert.Contains("task_fts", names);
            Assert.Contains("sync_state", names);
            Assert.Contains("application_settings", names);
            Assert.Contains("editor_settings", names);

            List<string> triggers = GetSchemaNames(db, "trigger");
            Assert.Contains("note_fts_insert", triggers);
            Assert.Contains("note_fts_delete", triggers);
            Assert.Contains("note_fts_update", triggers);
            Assert.Contains("task_fts_insert", triggers);
            Assert.Contains("task_fts_delete", triggers);
            Assert.Contains("task_fts_update", triggers);

            Assert.Equal(3, GetSchemaVersion(db));
        }

        [Fact]
        public void SharedMigrationsOmitClientState()
        {
            using TestDb db = new TestDb(MigrationSets.Shared());
            List<string> names = GetSchemaNames(db, "table");
            Assert.Contains("note", names);
            Assert.Contains("change_log", names);
            Assert.DoesNotContain("sync_state", names);
            Assert.DoesNotContain("application_settings", names);
            Assert.DoesNotContain("editor_settings", names);
            Assert.Equal(1, GetSchemaVersion(db));
        }

        [Fact]
        public void ServerMigrationsAddServerState()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            List<string> names = GetSchemaNames(db, "table");
            Assert.Contains("server_state", names);
            Assert.DoesNotContain("sync_state", names);
            Assert.DoesNotContain("application_settings", names);
            Assert.Equal(4, GetSchemaVersion(db));
        }

        [Fact]
        public void MigrationsAreIdempotentOnExistingDb()
        {
            using TestDb db = new TestDb();
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            DbMigrator migrator = new DbMigrator();
            foreach (IMigration migration in MigrationSets.Client())
            {
                migrator.Add(migration);
            }
            migrator.Run(connection);
            Assert.Equal(3, GetSchemaVersion(db));
        }

        private static List<string> GetSchemaNames(TestDb db, string type)
        {
            List<string> names = new List<string>();
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT name FROM sqlite_master WHERE type = @type";
            select.Parameters.AddWithValue("@type", type);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }

        private static int GetSchemaVersion(TestDb db)
        {
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT version FROM schema_version";
            return (int)(long)select.ExecuteScalar();
        }
    }
}
