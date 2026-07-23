using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.DataAccess;

namespace SylvaNote.Core.Search
{
    public sealed class SearchRepository
    {
        private readonly ConnectionManager _connectionManager;

        public SearchRepository(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public List<SearchResult> SearchNotes(string query)
        {
            return Search(query, "note", "note_fts");
        }

        public List<SearchResult> SearchTasks(string query)
        {
            return Search(query, "task", "task_fts");
        }

        private List<SearchResult> Search(string query, string table, string ftsTable)
        {
            List<SearchResult> results = new List<SearchResult>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = $@"
                SELECT {table}.id, {table}.title, snippet({ftsTable}, 1, '[', ']', '…', 12)
                FROM {ftsTable}
                JOIN {table} ON {table}.rowid = {ftsTable}.rowid
                WHERE {ftsTable} MATCH @query AND {table}.deleted = 0
                ORDER BY rank";
            select.Parameters.AddWithValue("@query", query);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SearchResult
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Snippet = reader.GetString(2),
                });
            }
            return results;
        }
    }
}
