using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess
{
    // Derived backlinks index (data.md): rebuilt from the markdown source on every save,
    // never in the change log. Link targets that don't exist locally are skipped - broken
    // links render broken in the body; the index only tracks resolvable targets.
    public static partial class NoteLinkRebuilder
    {
        private const string Ellipsis = "...";
        private const int DefaultSnippetRadius = 60;

        [GeneratedRegex(
            @"note://([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
            RegexOptions.CultureInvariant)]
        private static partial Regex LinkPattern();

        // The same link, with its markdown wrapper, so a snippet can show the link
        // text a reader would actually see rather than a raw uuid.
        [GeneratedRegex(
            @"!?\[([^\]]*)\]\(note://([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)",
            RegexOptions.CultureInvariant)]
        private static partial Regex MarkdownLinkPattern();

        [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
        private static partial Regex WhitespaceRuns();

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
                foreach (Match match in LinkPattern().Matches(body))
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

        // Text around the first link to targetNoteId, for a backlink card. Markdown
        // links collapse to their link text so the snippet reads as prose; whitespace
        // collapses first so the window is measured in visible characters.
        public static string ContextSnippet(string body, string targetNoteId, int radius = DefaultSnippetRadius)
        {
            string snippet = string.Empty;

            if (!string.IsNullOrWhiteSpace(body) && !string.IsNullOrEmpty(targetNoteId))
            {
                string collapsed = WhitespaceRuns().Replace(body, " ").Trim();
                StringBuilder flattened = new StringBuilder();
                int targetStart = -1;
                int targetEnd = 0;
                int cursor = 0;

                foreach (Match match in MarkdownLinkPattern().Matches(collapsed))
                {
                    flattened.Append(collapsed, cursor, match.Index - cursor);
                    string text = match.Groups[1].Value;
                    if (targetStart < 0 && string.Equals(match.Groups[2].Value, targetNoteId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetStart = flattened.Length;
                        targetEnd = targetStart + text.Length;
                    }
                    flattened.Append(text);
                    cursor = match.Index + match.Length;
                }
                flattened.Append(collapsed, cursor, collapsed.Length - cursor);

                string flat = flattened.ToString();
                if (targetStart < 0)
                {
                    // A bare note:// url, not wrapped in markdown - centre on the url.
                    string bare = "note://" + targetNoteId;
                    targetStart = flat.IndexOf(bare, StringComparison.OrdinalIgnoreCase);
                    targetEnd = targetStart < 0 ? 0 : targetStart + bare.Length;
                }

                snippet = targetStart < 0 ? Head(flat, radius * 2) : Window(flat, targetStart, targetEnd, radius);
            }

            return snippet;
        }

        private static string Window(string text, int start, int end, int radius)
        {
            int from = Math.Max(0, start - radius);
            int to = Math.Min(text.Length, end + radius);
            string window = text.Substring(from, to - from);
            return (from > 0 ? Ellipsis : string.Empty) + window + (to < text.Length ? Ellipsis : string.Empty);
        }

        private static string Head(string text, int length)
        {
            return text.Length <= length ? text : text.Substring(0, length) + Ellipsis;
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
