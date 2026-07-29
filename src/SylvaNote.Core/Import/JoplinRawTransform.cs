using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SylvaNote.Core.Export;

namespace SylvaNote.Core.Import
{
    // Rewrites a Joplin RAW export ("RAW - Joplin Export Directory": flat id-named
    // markdown files with a metadata trailer, hierarchy in parent_id, blobs in
    // resources/) into the directory-tree-with-front-matter shape
    // MarkdownImportBuilder reads, so RAW is a fourth source rather than a second
    // importer. Joplin ids are 32 hex digits - a valid GUID - so they ride through
    // as sylvanote-ids, which makes RAW re-imports merge and lets note-to-note
    // :/id links resolve; the MD front matter export can do neither
    // (features/import.md).
    public static class JoplinRawTransform
    {
        public sealed class Result
        {
            public bool IsRaw { get; set; }

            public List<ImportFile> Files { get; set; }

            public List<string> Warnings { get; set; }
        }

        private const string ResourcesDirectory = "resources";
        private const int TypeNote = 1;
        private const int TypeNotebook = 2;
        private const int TypeResource = 4;

        private static readonly Regex HexIdPattern = new Regex(
            @"^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);

        private static readonly Regex MetadataLinePattern = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*:( .*)?$", RegexOptions.CultureInvariant);

        private static readonly Regex InternalLinkPattern = new Regex(
            @":/([0-9a-fA-F]{32})", RegexOptions.CultureInvariant);

        private sealed class RawItem
        {
            public string Id { get; set; }
            public int Type { get; set; }
            public string Title { get; set; }
            public string Body { get; set; }
            public string ParentId { get; set; }
            public string Created { get; set; }
            public string Updated { get; set; }
            public bool Deleted { get; set; }
            public bool Encrypted { get; set; }
        }

        public static Result Apply(List<ImportFile> files)
        {
            Result result = new Result
            {
                IsRaw = false,
                Files = files ?? new List<ImportFile>(),
                Warnings = new List<string>(),
            };

            List<RawItem> items = new List<RawItem>();
            List<ImportFile> resourceFiles = new List<ImportFile>();
            List<ImportFile> passThrough = new List<ImportFile>();
            bool disqualified = false;

            foreach (ImportFile file in result.Files)
            {
                string path = (file?.Path ?? string.Empty).Replace('\\', '/').Trim('/');
                if (path.Length == 0 || HasDotSegment(path))
                {
                    passThrough.Add(file);
                }
                else if (path.StartsWith(ResourcesDirectory + "/", StringComparison.OrdinalIgnoreCase))
                {
                    resourceFiles.Add(file);
                }
                else if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    // RAW is flat: every markdown file sits at the root, named by its
                    // id, and ends in a metadata trailer. One that does not fit means
                    // this is some other folder of markdown - leave it alone.
                    string stem = path.Substring(0, path.Length - 3);
                    RawItem item = path.IndexOf('/') < 0 && HexIdPattern.IsMatch(stem)
                        ? ParseItem(stem, file.Content)
                        : null;
                    if (item == null)
                    {
                        disqualified = true;
                    }
                    else
                    {
                        items.Add(item);
                    }
                }
                else
                {
                    passThrough.Add(file);
                }
            }

            bool hasContent = false;
            foreach (RawItem item in items)
            {
                if (item.Type == TypeNote || item.Type == TypeNotebook)
                {
                    hasContent = true;
                }
            }

            if (!disqualified && hasContent)
            {
                result.IsRaw = true;
                result.Files = Rebuild(items, resourceFiles, passThrough, result.Warnings);
            }

            return result;
        }

        private static List<ImportFile> Rebuild(
            List<RawItem> items,
            List<ImportFile> resourceFiles,
            List<ImportFile> passThrough,
            List<string> warnings)
        {
            Dictionary<string, RawItem> notebooks = new Dictionary<string, RawItem>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, RawItem> resourceMeta = new Dictionary<string, RawItem>(StringComparer.OrdinalIgnoreCase);
            List<RawItem> notes = new List<RawItem>();
            int encrypted = 0;
            int trashed = 0;
            int internals = 0;

            foreach (RawItem item in items)
            {
                if (item.Encrypted)
                {
                    encrypted++;
                }
                else if (item.Type == TypeNotebook)
                {
                    if (item.Deleted)
                    {
                        trashed++;
                    }
                    else
                    {
                        notebooks[item.Id] = item;
                    }
                }
                else if (item.Type == TypeNote)
                {
                    if (item.Deleted)
                    {
                        trashed++;
                    }
                    else
                    {
                        notes.Add(item);
                    }
                }
                else if (item.Type == TypeResource)
                {
                    resourceMeta[item.Id] = item;
                }
                else
                {
                    internals++;
                }
            }

            Dictionary<string, string> guidById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (RawItem notebook in notebooks.Values)
            {
                guidById[notebook.Id] = ToGuid(notebook.Id);
            }
            foreach (RawItem note in notes)
            {
                guidById[note.Id] = ToGuid(note.Id);
            }

            List<ImportFile> output = new List<ImportFile>(passThrough);
            Dictionary<string, string> resourcePathById = EmitResources(resourceFiles, resourceMeta, output);

            Dictionary<string, List<RawItem>> notebooksByParent = GroupByParent(notebooks.Values, notebooks);
            Dictionary<string, List<RawItem>> notesByParent = GroupByParent(notes, notebooks);

            HashSet<string> emittedNotebooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EmitChildren(string.Empty, string.Empty, rootNames, notebooksByParent, notesByParent,
                guidById, resourcePathById, emittedNotebooks, output);

            // A parent_id cycle makes a notebook unreachable from the root walk;
            // stranding it at the root beats dropping its notes.
            bool remaining = true;
            while (remaining)
            {
                RawItem stray = null;
                foreach (RawItem notebook in notebooks.Values)
                {
                    if (stray == null && !emittedNotebooks.Contains(notebook.Id))
                    {
                        stray = notebook;
                    }
                }
                if (stray == null)
                {
                    remaining = false;
                }
                else
                {
                    string name = ExportFileName.Unique(ExportFileName.Sanitize(stray.Title), string.Empty, rootNames);
                    emittedNotebooks.Add(stray.Id);
                    output.Add(NoteFile(name + ".md", stray, guidById, resourcePathById));
                    EmitChildren(name, stray.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        notebooksByParent, notesByParent, guidById, resourcePathById, emittedNotebooks, output);
                }
            }

            if (encrypted > 0)
            {
                warnings.Add("Skipped " + Count(encrypted, "encrypted Joplin item") + "; decrypt them in Joplin before exporting.");
            }
            if (trashed > 0)
            {
                warnings.Add("Skipped " + Count(trashed, "item") + " in Joplin's trash.");
            }
            if (internals > 0)
            {
                warnings.Add("Skipped " + Count(internals, "Joplin internal item") + " (tags and other metadata).");
            }

            return output;
        }

        // Resource files are named <id>.<ext> on disk; the sidecar metadata carries
        // the human filename, so the imported attachment gets a real name. Path is
        // renamed, SourcePath keeps the on-disk name for the applier's read.
        private static Dictionary<string, string> EmitResources(
            List<ImportFile> resourceFiles,
            Dictionary<string, RawItem> resourceMeta,
            List<ImportFile> output)
        {
            Dictionary<string, string> resourcePathById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ImportFile file in resourceFiles)
            {
                string sourcePath = file.Path.Replace('\\', '/').Trim('/');
                string name = LastSegment(sourcePath);
                string stem = Stem(name);

                RawItem meta;
                if (HexIdPattern.IsMatch(stem) && resourceMeta.TryGetValue(stem, out meta)
                    && !string.IsNullOrEmpty(meta.Title))
                {
                    string extension = Extension(name);
                    name = meta.Title.IndexOf('.') > 0 || extension.Length == 0 ? meta.Title : meta.Title + extension;
                }

                string sanitized = ExportFileName.SanitizeAttachment(name);
                string unique = ExportFileName.Unique(Stem(sanitized), Extension(sanitized), used);
                string path = ResourcesDirectory + "/" + unique;

                if (HexIdPattern.IsMatch(stem))
                {
                    resourcePathById[stem] = path;
                }
                output.Add(new ImportFile
                {
                    Path = path,
                    SourcePath = sourcePath,
                    SizeBytes = file.SizeBytes,
                });
            }

            return resourcePathById;
        }

        private static Dictionary<string, List<RawItem>> GroupByParent(
            IEnumerable<RawItem> items,
            Dictionary<string, RawItem> notebooks)
        {
            Dictionary<string, List<RawItem>> result = new Dictionary<string, List<RawItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (RawItem item in items)
            {
                // An unknown parent makes the item a root, mirroring the export
                // builder's out-of-scope-parent rule.
                string key = !string.IsNullOrEmpty(item.ParentId) && notebooks.ContainsKey(item.ParentId)
                    ? item.ParentId
                    : string.Empty;
                if (!result.ContainsKey(key))
                {
                    result[key] = new List<RawItem>();
                }
                result[key].Add(item);
            }

            foreach (List<RawItem> siblings in result.Values)
            {
                siblings.Sort(CompareItems);
            }

            return result;
        }

        private static int CompareItems(RawItem left, RawItem right)
        {
            int result = string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
            if (result == 0)
            {
                result = string.CompareOrdinal(left.Id, right.Id);
            }
            return result;
        }

        private static void EmitChildren(
            string directory,
            string parentId,
            HashSet<string> used,
            Dictionary<string, List<RawItem>> notebooksByParent,
            Dictionary<string, List<RawItem>> notesByParent,
            Dictionary<string, string> guidById,
            Dictionary<string, string> resourcePathById,
            HashSet<string> emittedNotebooks,
            List<ImportFile> output)
        {
            List<RawItem> childNotebooks;
            if (notebooksByParent.TryGetValue(parentId, out childNotebooks))
            {
                foreach (RawItem notebook in childNotebooks)
                {
                    string name = ExportFileName.Unique(ExportFileName.Sanitize(notebook.Title), string.Empty, used);
                    string folder = directory.Length == 0 ? name : directory + "/" + name;
                    emittedNotebooks.Add(notebook.Id);
                    output.Add(NoteFile(folder + ".md", notebook, guidById, resourcePathById));
                    EmitChildren(folder, notebook.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        notebooksByParent, notesByParent, guidById, resourcePathById, emittedNotebooks, output);
                }
            }

            List<RawItem> childNotes;
            if (notesByParent.TryGetValue(parentId, out childNotes))
            {
                foreach (RawItem note in childNotes)
                {
                    string name = ExportFileName.Unique(ExportFileName.Sanitize(note.Title), string.Empty, used);
                    string path = (directory.Length == 0 ? name : directory + "/" + name) + ".md";
                    output.Add(NoteFile(path, note, guidById, resourcePathById));
                }
            }
        }

        private static ImportFile NoteFile(
            string path,
            RawItem item,
            Dictionary<string, string> guidById,
            Dictionary<string, string> resourcePathById)
        {
            int depth = 0;
            foreach (char character in path)
            {
                if (character == '/')
                {
                    depth++;
                }
            }

            string body = item.Type == TypeNote
                ? RewriteLinks(item.Body, depth, guidById, resourcePathById)
                : string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("title: ").Append(MarkdownExportBuilder.YamlScalar(item.Title)).Append('\n');
            if (!string.IsNullOrEmpty(item.Created))
            {
                builder.Append("created: ").Append(item.Created).Append('\n');
            }
            if (!string.IsNullOrEmpty(item.Updated))
            {
                builder.Append("updated: ").Append(item.Updated).Append('\n');
            }
            builder.Append("sylvanote-id: ").Append(ToGuid(item.Id)).Append('\n');
            builder.Append("---\n\n");
            builder.Append(body);

            string content = builder.ToString();
            return new ImportFile { Path = path, Content = content, SizeBytes = content.Length };
        }

        private static string RewriteLinks(
            string body,
            int depth,
            Dictionary<string, string> guidById,
            Dictionary<string, string> resourcePathById)
        {
            string result = body ?? string.Empty;

            if (result.Length > 0)
            {
                result = InternalLinkPattern.Replace(result, delegate (Match match)
                {
                    string id = match.Groups[1].Value;
                    string replacement = match.Value;
                    string guid;
                    string resourcePath;

                    if (guidById.TryGetValue(id, out guid))
                    {
                        replacement = "note://" + guid;
                    }
                    else if (resourcePathById.TryGetValue(id, out resourcePath))
                    {
                        StringBuilder relative = new StringBuilder();
                        for (int index = 0; index < depth; index++)
                        {
                            relative.Append("../");
                        }
                        replacement = relative.Append(EncodePath(resourcePath)).ToString();
                    }

                    // Anything else is a link to an item outside the export; the
                    // builder's own :/id handling reports it.
                    return replacement;
                });
            }

            return result;
        }

        // Only the characters that would end a markdown link target early, matching
        // the export builder's escaping.
        private static string EncodePath(string path)
        {
            return path
                .Replace("%", "%25")
                .Replace(" ", "%20")
                .Replace("(", "%28")
                .Replace(")", "%29");
        }

        private static RawItem ParseItem(string stem, string content)
        {
            RawItem result = null;
            string text = (content ?? string.Empty).Replace("\r\n", "\n");
            string[] lines = text.Split('\n');

            int end = lines.Length;
            while (end > 0 && lines[end - 1].Trim().Length == 0)
            {
                end--;
            }
            int start = end;
            while (start > 0 && MetadataLinePattern.IsMatch(lines[start - 1]))
            {
                start--;
            }

            if (start < end)
            {
                Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int index = start; index < end; index++)
                {
                    int colon = lines[index].IndexOf(':');
                    string key = lines[index].Substring(0, colon);
                    metadata[key] = lines[index].Substring(colon + 1).Trim();
                }

                string id;
                string type;
                int parsedType;
                if (metadata.TryGetValue("id", out id) && string.Equals(id, stem, StringComparison.OrdinalIgnoreCase)
                    && metadata.TryGetValue("type_", out type)
                    && int.TryParse(type, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedType))
                {
                    int bodyEnd = start;
                    while (bodyEnd > 0 && lines[bodyEnd - 1].Trim().Length == 0)
                    {
                        bodyEnd--;
                    }
                    int bodyStart = 1;
                    if (bodyStart < bodyEnd && lines[bodyStart].Trim().Length == 0)
                    {
                        bodyStart++;
                    }

                    result = new RawItem
                    {
                        Id = id,
                        Type = parsedType,
                        Title = bodyEnd > 0 ? lines[0].Trim() : string.Empty,
                        Body = bodyStart < bodyEnd
                            ? string.Join("\n", lines, bodyStart, bodyEnd - bodyStart)
                            : string.Empty,
                        ParentId = Value(metadata, "parent_id"),
                        // The user_* pair is what Joplin shows and lets the user edit;
                        // the plain pair is bookkeeping.
                        Created = FirstValue(metadata, "user_created_time", "created_time"),
                        Updated = FirstValue(metadata, "user_updated_time", "updated_time"),
                        Deleted = IsNonZero(Value(metadata, "deleted_time")),
                        Encrypted = Value(metadata, "encryption_applied") == "1",
                    };
                }
            }

            return result;
        }

        private static string Value(Dictionary<string, string> metadata, string key)
        {
            string result;
            metadata.TryGetValue(key, out result);
            return result ?? string.Empty;
        }

        private static string FirstValue(Dictionary<string, string> metadata, string preferred, string fallback)
        {
            string result = Value(metadata, preferred);
            if (result.Length == 0)
            {
                result = Value(metadata, fallback);
            }
            return result;
        }

        private static bool IsNonZero(string value)
        {
            return value.Length > 0 && value != "0";
        }

        private static string ToGuid(string hexId)
        {
            return Guid.ParseExact(hexId, "N").ToString("D");
        }

        private static bool HasDotSegment(string path)
        {
            bool result = false;
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment[0] == '.')
                {
                    result = true;
                }
            }
            return result;
        }

        private static string LastSegment(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        private static string Stem(string filename)
        {
            int dot = filename.LastIndexOf('.');
            return dot > 0 ? filename.Substring(0, dot) : filename;
        }

        private static string Extension(string filename)
        {
            int dot = filename.LastIndexOf('.');
            return dot > 0 ? filename.Substring(dot) : string.Empty;
        }

        private static string Count(int count, string word)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + word + (count == 1 ? string.Empty : "s");
        }
    }
}
