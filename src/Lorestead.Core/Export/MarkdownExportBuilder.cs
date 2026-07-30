using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Export
{
    // Turns a set of notes into the file layout features/export.md describes: a note
    // with children becomes Parent.md beside a sibling Parent/ folder, attachments go
    // in one root folder, and every note carries YAML front matter. Nothing here
    // touches the file system - the caller writes the plan out, so the whole shape of
    // an export is unit-testable in memory.
    public static partial class MarkdownExportBuilder
    {
        public const string AttachmentsDirectory = "attachments";
        public const string TemplatesDirectory = "Templates";

        private const string NoteExtension = ".md";

        // Only the parenthesised URL of a markdown link is rewritten; the [Text] half
        // is left exactly as written. A bare note:// url outside a link stays bare -
        // a relative path on its own would not be a link in either target app.
        [GeneratedRegex(
            @"\((note|attachment)://([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)",
            RegexOptions.CultureInvariant)]
        private static partial Regex SchemeLinkPattern();

        public static ExportLayout Build(ExportSource source)
        {
            Dictionary<string, Note> byId = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
            foreach (Note note in source?.Notes ?? new List<Note>())
            {
                if (note != null && !note.Deleted && !string.IsNullOrEmpty(note.Id))
                {
                    byId[note.Id] = note;
                }
            }

            Dictionary<string, List<Note>> childrenByParent = new Dictionary<string, List<Note>>(StringComparer.OrdinalIgnoreCase);
            List<Note> normalRoots = new List<Note>();
            List<Note> templateRoots = new List<Note>();

            foreach (Note note in byId.Values)
            {
                // A note whose parent is out of scope is a root of this export, which
                // is what makes a subtree and the whole tree the same walk.
                if (!string.IsNullOrEmpty(note.ParentId) && byId.ContainsKey(note.ParentId))
                {
                    if (!childrenByParent.ContainsKey(note.ParentId))
                    {
                        childrenByParent[note.ParentId] = new List<Note>();
                    }
                    childrenByParent[note.ParentId].Add(note);
                }
                else if (source.GroupTemplates && note.Type == NoteType.Template)
                {
                    templateRoots.Add(note);
                }
                else
                {
                    normalRoots.Add(note);
                }
            }

            Dictionary<string, string> pathById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();
            HashSet<string> rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (templateRoots.Count > 0)
            {
                // Claim the section folder before any note can sanitize into it.
                rootNames.Add(TemplatesDirectory);
            }

            AssignPaths(string.Empty, normalRoots, childrenByParent, rootNames, pathById, order);
            if (templateRoots.Count > 0)
            {
                AssignPaths(TemplatesDirectory, templateRoots, childrenByParent,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase), pathById, order);
            }

            List<ExportedAttachment> attachments = new List<ExportedAttachment>();
            Dictionary<string, string> attachmentPathById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> attachmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Attachment attachment in source?.Attachments ?? new List<Attachment>())
            {
                if (attachment != null && !attachment.Deleted
                    && !string.IsNullOrEmpty(attachment.NoteId) && pathById.ContainsKey(attachment.NoteId))
                {
                    string full = ExportFileName.SanitizeAttachment(attachment.Filename);
                    int dot = full.LastIndexOf('.');
                    string stem = dot > 0 ? full.Substring(0, dot) : full;
                    string extension = dot > 0 ? full.Substring(dot) : string.Empty;
                    string path = AttachmentsDirectory + "/" + ExportFileName.Unique(stem, extension, attachmentNames);

                    attachmentPathById[attachment.Id] = path;
                    attachments.Add(new ExportedAttachment { Id = attachment.Id, Path = path });
                }
            }

            List<ExportedNote> notes = new List<ExportedNote>();
            foreach (string id in order)
            {
                Note note = byId[id];
                string path = pathById[id];
                notes.Add(new ExportedNote
                {
                    Id = id,
                    Path = path,
                    Content = FrontMatter(note) + RewriteLinks(note.Body, DirectoryOf(path), pathById, attachmentPathById),
                });
            }

            return new ExportLayout { Notes = notes, Attachments = attachments };
        }

        private static void AssignPaths(
            string directory,
            List<Note> siblings,
            Dictionary<string, List<Note>> childrenByParent,
            HashSet<string> used,
            Dictionary<string, string> pathById,
            List<string> order)
        {
            siblings.Sort(ComparePosition);

            foreach (Note note in siblings)
            {
                string name = ExportFileName.Unique(ExportFileName.Sanitize(note.Title), string.Empty, used);
                string folder = directory.Length == 0 ? name : directory + "/" + name;

                pathById[note.Id] = folder + NoteExtension;
                order.Add(note.Id);

                List<Note> children;
                if (childrenByParent.TryGetValue(note.Id, out children) && children.Count > 0)
                {
                    AssignPaths(folder, children, childrenByParent,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase), pathById, order);
                }
            }
        }

        private static int ComparePosition(Note left, Note right)
        {
            int result = string.CompareOrdinal(left.Position ?? string.Empty, right.Position ?? string.Empty);
            if (result == 0)
            {
                result = string.CompareOrdinal(left.Id ?? string.Empty, right.Id ?? string.Empty);
            }
            return result;
        }

        private static string FrontMatter(Note note)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("title: ").Append(YamlScalar(note.Title)).Append('\n');

            string created = FrontMatterDate(note.CreatedAt);
            if (created.Length > 0)
            {
                builder.Append("created: ").Append(created).Append('\n');
            }

            string updated = FrontMatterDate(note.UpdatedAt);
            if (updated.Length > 0)
            {
                builder.Append("updated: ").Append(updated).Append('\n');
            }

            builder.Append("lorestead-id: ").Append(note.Id).Append('\n');
            builder.Append("---\n\n");
            return builder.ToString();
        }

        // Joplin's spelling: ISO 8601 in UTC with the T replaced by a space. Rows are
        // stored round-trip ("O"), so this is a reformat, not a reinterpretation.
        private static string FrontMatterDate(string iso)
        {
            string result = string.Empty;
            DateTime parsed;

            if (!string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                result = parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";
            }

            return result;
        }

        // Titles are free-form, so anything that would turn a plain scalar into a list
        // item, a mapping, a comment or an anchor gets quoted instead. Public because
        // the Joplin RAW import transform writes the same front matter.
        public static string YamlScalar(string value)
        {
            string result = value ?? string.Empty;
            bool quote = result.Length == 0;

            if (!quote)
            {
                quote = "-?:,[]{}#&*!|>'\"%@`".IndexOf(result[0]) >= 0
                    || char.IsWhiteSpace(result[0])
                    || char.IsWhiteSpace(result[result.Length - 1])
                    || result.IndexOf(": ", StringComparison.Ordinal) >= 0
                    || result.IndexOf(" #", StringComparison.Ordinal) >= 0
                    || result.EndsWith(":", StringComparison.Ordinal)
                    || result.IndexOf('\n') >= 0;
            }

            if (quote)
            {
                result = "\"" + result.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            return result;
        }

        // An out-of-scope note:// is deliberately left alone: a dead link says "this
        // pointed outside the export", and rewriting it would write something false.
        private static string RewriteLinks(
            string body,
            string directory,
            Dictionary<string, string> notePaths,
            Dictionary<string, string> attachmentPaths)
        {
            string result = body ?? string.Empty;

            if (result.Length > 0)
            {
                result = SchemeLinkPattern().Replace(result, delegate (Match match)
                {
                    string replacement = match.Value;
                    Dictionary<string, string> paths = match.Groups[1].Value == "note" ? notePaths : attachmentPaths;
                    string target;
                    if (paths.TryGetValue(match.Groups[2].Value, out target))
                    {
                        replacement = "(" + Relative(directory, target) + ")";
                    }
                    return replacement;
                });
            }

            return result;
        }

        private static string DirectoryOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        private static string Relative(string fromDirectory, string toPath)
        {
            string[] from = fromDirectory.Length == 0 ? new string[0] : fromDirectory.Split('/');
            string[] to = toPath.Split('/');

            int common = 0;
            while (common < from.Length && common < to.Length - 1
                && string.Equals(from[common], to[common], StringComparison.Ordinal))
            {
                common++;
            }

            StringBuilder builder = new StringBuilder();
            for (int index = common; index < from.Length; index++)
            {
                builder.Append("../");
            }
            for (int index = common; index < to.Length; index++)
            {
                if (index > common)
                {
                    builder.Append('/');
                }
                builder.Append(EscapeSegment(to[index]));
            }

            return builder.ToString();
        }

        // Only the characters that would end the markdown link target early. Everything
        // else illegal in a path was already stripped by the sanitizer.
        private static string EscapeSegment(string segment)
        {
            return segment
                .Replace("%", "%25")
                .Replace(" ", "%20")
                .Replace("(", "%28")
                .Replace(")", "%29");
        }
    }
}
