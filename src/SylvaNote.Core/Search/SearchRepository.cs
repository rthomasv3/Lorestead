using System.Collections.Generic;
using System.Text;
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

        // limit 0 means unlimited - the app's own search dialog wants every hit,
        // agents cap it to keep results out of their context window.
        public List<SearchResult> SearchNotes(string query, bool includeTrashed = false, int limit = 0)
        {
            return Search(query, "note", "note_fts", includeTrashed, limit);
        }

        public List<SearchResult> SearchTasks(string query, int limit = 0)
        {
            return Search(query, "task", "task_fts", includeTrashed: false, limit);
        }

        // Task hits carry their board/column so the dialog can render the
        // `Board › List › Task` breadcrumb without loading every board.
        public List<TaskSearchResult> SearchTasksWithContext(string query, int limit = 0)
        {
            List<TaskSearchResult> results = new List<TaskSearchResult>();
            string match = BuildMatchQuery(query);

            if (match.Length > 0)
            {
                using SqliteConnection connection = _connectionManager.CreateConnection();
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = @"
                    SELECT task.id, task.title, snippet(task_fts, 1, '[', ']', '...', 12),
                           bc.id, bc.name, b.id, b.name, task.updated_at
                    FROM task_fts
                    JOIN task ON task.rowid = task_fts.rowid
                    JOIN board_column bc ON bc.id = task.column_id
                    JOIN board b ON b.id = bc.board_id
                    WHERE task_fts MATCH @query AND task.deleted = 0 AND bc.deleted = 0 AND b.deleted = 0
                    ORDER BY rank" + LimitClause(limit);
                select.Parameters.AddWithValue("@query", match);
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new TaskSearchResult
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        Snippet = reader.GetString(2),
                        ColumnId = reader.GetString(3),
                        ColumnName = reader.GetString(4),
                        BoardId = reader.GetString(5),
                        BoardName = reader.GetString(6),
                        UpdatedAt = reader.GetString(7),
                    });
                }
            }
            return results;
        }

        // Boards are a handful of named rows - plain substring match, no FTS
        // (features/search.md). LIKE wildcards in the query are escaped.
        public List<SearchResult> SearchBoards(string query, int limit = 0)
        {
            List<SearchResult> results = new List<SearchResult>();
            string trimmed = (query ?? string.Empty).Trim();

            if (trimmed.Length > 0)
            {
                string escaped = trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                using SqliteConnection connection = _connectionManager.CreateConnection();
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = @"
                    SELECT id, name FROM board
                    WHERE deleted = 0 AND name LIKE @pattern ESCAPE '\'
                    ORDER BY name" + LimitClause(limit);
                select.Parameters.AddWithValue("@pattern", "%" + escaped + "%");
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new SearchResult
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        Snippet = string.Empty,
                    });
                }
            }
            return results;
        }

        private List<SearchResult> Search(string query, string table, string ftsTable, bool includeTrashed, int limit)
        {
            List<SearchResult> results = new List<SearchResult>();
            string match = BuildMatchQuery(query);

            if (match.Length > 0)
            {
                string deletedFilter = includeTrashed ? string.Empty : $" AND {table}.deleted = 0";
                using SqliteConnection connection = _connectionManager.CreateConnection();
                using SqliteCommand select = connection.CreateCommand();
                select.CommandText = $@"
                    SELECT {table}.id, {table}.title, snippet({ftsTable}, 1, '[', ']', '...', 12)
                    FROM {ftsTable}
                    JOIN {table} ON {table}.rowid = {ftsTable}.rowid
                    WHERE {ftsTable} MATCH @query{deletedFilter}
                    ORDER BY rank{LimitClause(limit)}";
                select.Parameters.AddWithValue("@query", match);
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
            }
            return results;
        }

        // Inlined rather than bound: the value is an int, so it cannot carry an
        // injection, and 0 has to drop the clause entirely rather than bind a value.
        private static string LimitClause(int limit)
        {
            return limit > 0 ? " LIMIT " + limit : string.Empty;
        }

        // User text goes in as quoted prefix tokens ("term"*) so FTS5 query syntax
        // characters ((), ", -, NEAR...) can never throw, and type-ahead matches partials.
        private static string BuildMatchQuery(string query)
        {
            StringBuilder match = new StringBuilder();
            foreach (string token in (query ?? string.Empty).Split(' ', '\t', '\r', '\n'))
            {
                if (token.Length > 0)
                {
                    if (match.Length > 0)
                    {
                        match.Append(' ');
                    }
                    match.Append('"').Append(token.Replace("\"", "\"\"")).Append("\"*");
                }
            }
            return match.ToString();
        }
    }
}
