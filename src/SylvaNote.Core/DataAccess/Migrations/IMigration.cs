using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess.Migrations
{
    public interface IMigration
    {
        int Version { get; }
        void Up(SqliteConnection connection);
    }
}
