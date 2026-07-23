using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SylvaNote.Core.DataAccess
{
    // Derived backlinks index (data.md): rebuilt from the markdown source on every save,
    // never in the change log. Link targets that don't exist locally are skipped — broken
    // links render broken in the body; the index only tracks resolvable targets.
    public static class NoteLinkRebuilder
    {
        private static readonly Regex LinkPattern = new Regex(
            @"note://([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
            RegexOptions.CultureInvariant);

        public static void RebuildForNoteWithin(SqliteConnection connection, SqliteTransaction transaction, string noteId, string body)
        {
            Rebuild(connection, "from_note_id", noteId, body);
        }

        public static void RebuildForTaskWithin(SqliteConnection connection, SqliteTransaction transaction, string taskId, string body)
        {
            Rebuild(connection, "from_task_id", taskId, body);
        }

        public static IReadOnlyList<string> ParseTargets(string body)
        {
            List<string> targets = new List<string>();
            if (!string.IsNullOrEmpty(body))
            {
                foreach (Match match in LinkPattern.Matches(body))
                {
                    string id = match.Groups[1].Value.ToLowerInvariant();
                    if (!targets.Contains(id))
                    {
                        targets.Add(id);
                    }
                }
            }
            return targets;
        }

        private static void Rebuild(SqliteConnection connection, string fromColumn, string fromId, string body)
        {
            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.CommandText = $"DELETE FROM note_link WHERE {fromColumn} = @from_id";
                delete.Parameters.AddWithValue("@from_id", fromId);
                delete.ExecuteNonQuery();
            }

            foreach (string target in ParseTargets(body))
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = $@"
                    INSERT INTO note_link ({fromColumn}, to_note_id)
                    SELECT @from_id, @to_id WHERE EXISTS (SELECT 1 FROM note WHERE id = @to_id)";
                insert.Parameters.AddWithValue("@from_id", fromId);
                insert.Parameters.AddWithValue("@to_id", target);
                insert.ExecuteNonQuery();
            }
        }
    }
}
