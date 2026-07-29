using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    public interface IMigration
    {
        int Version { get; }
        void Up(SqliteConnection connection);
    }
}
