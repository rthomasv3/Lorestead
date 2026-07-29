using System;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Lorestead.Core.DataAccess;
using Lorestead.Core.DataAccess.Migrations;
using Xunit;

namespace Lorestead.IntegrationTests
{
    // File-based on purpose: encryption at rest is only observable in the bytes on disk.
    public sealed class EncryptionTests : IDisposable
    {
        private readonly string _dbPath;

        public EncryptionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lorestead-enc-{Guid.NewGuid():N}.db");
        }

        public void Dispose()
        {
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(_dbPath), Path.GetFileName(_dbPath) + "*"))
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void KeyedDatabaseIsEncryptedOnDiskAndRejectsWrongKeys()
        {
            ConnectionManager manager = new ConnectionManager();
            manager.Open(_dbPath, MigrationSets.Shared(), "correct-key");
            using (SqliteConnection connection = manager.CreateConnection())
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO note (id, title, body, position, type, deleted, created_at, updated_at) " +
                    "VALUES ('n1', 'Secret title', 'secret body', 'V', 0, 0, 't', 't')";
                insert.ExecuteNonQuery();
            }
            manager.Close();

            // Plain SQLite files start with "SQLite format 3"; SQLCipher files start
            // with the random KDF salt.
            byte[] header = new byte[16];
            using (FileStream stream = File.OpenRead(_dbPath))
            {
                stream.ReadExactly(header, 0, header.Length);
            }
            Assert.NotEqual("SQLite format 3\0", Encoding.ASCII.GetString(header));

            ConnectionManager wrongKey = new ConnectionManager();
            Assert.ThrowsAny<SqliteException>(() => wrongKey.Open(_dbPath, MigrationSets.Shared(), "wrong-key"));
            wrongKey.Close();

            ConnectionManager reopened = new ConnectionManager();
            reopened.Open(_dbPath, MigrationSets.Shared(), "correct-key");
            using (SqliteConnection connection = reopened.CreateConnection())
            {
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = "SELECT title FROM note WHERE id = 'n1'";
                Assert.Equal("Secret title", select.ExecuteScalar());
            }
            reopened.Close();
        }
    }
}
