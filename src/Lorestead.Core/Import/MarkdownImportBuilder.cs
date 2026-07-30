using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Lorestead.Core.Entities;
using Lorestead.Core.Export;
using Lorestead.Core.Notes;

namespace Lorestead.Core.Import
{
    // Turns a directory tree of markdown (a Lorestead export, an Obsidian vault, or
    // a Joplin markdown export) into an ImportPlan: the inverse of
    // MarkdownExportBuilder. Nothing here touches the file system or the database -
    // the caller supplies the files and the existing notes, and applies the plan -
    // so the whole shape of an import is unit-testable in memory.
    public static partial class MarkdownImportBuilder
    {
        private const string NoteExtension = ".md";
        private const string JoplinResourcesDirectory = "_resources";

        // Matches the client-side attachment limit; an oversize file keeps its link
        // untouched and lands in the report instead of failing the import.
        private const long MaxAttachmentBytes = 100L * 1024 * 1024;

        [GeneratedRegex(@"\]\(([^)\n]*)\)", RegexOptions.CultureInvariant)]
        private static partial Regex MarkdownLinkPattern();

        [GeneratedRegex(@"(!?)\[\[([^\[\]\n]+)\]\]", RegexOptions.CultureInvariant)]
        private static partial Regex WikiLinkPattern();

        [GeneratedRegex(@"^:/([0-9a-fA-F]{32})$", RegexOptions.CultureInvariant)]
        private static partial Regex JoplinLinkPattern();

        [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
        private static partial Regex SchemePattern();

        // The " (2)" the export appends when sibling filenames collide, so a
        // re-imported attachment can still match the original it came from.
        [GeneratedRegex(@" \(\d+\)$", RegexOptions.CultureInvariant)]
        private static partial Regex CopySuffixPattern();

        private sealed class Node
        {
            public string Key { get; set; }
            public string ParentKey { get; set; }
            public string Name { get; set; }
            public bool InTemplates { get; set; }
            public ImportFile File { get; set; }
            public ImportFrontMatter.Result FrontMatter { get; set; }
            public string Title { get; set; }
            public string FinalId { get; set; }
            public Note Existing { get; set; }
            public bool HadId { get; set; }
        }

        private sealed class Context
        {
            public List<string> Warnings { get; set; }
            public Dictionary<string, int> DroppedKeys { get; set; }
            public Dictionary<string, ImportFile> OthersByPath { get; set; }
            public Dictionary<string, string> NoteIdByMdPath { get; set; }
            public Dictionary<string, List<string>> MdPathsByStem { get; set; }
            public Dictionary<string, List<string>> OtherPathsByName { get; set; }
            public Dictionary<string, string> ResourcePathByStem { get; set; }
            public HashSet<string> ValidNoteIds { get; set; }
            public Dictionary<string, List<string>> ExistingNoteIdsByTitle { get; set; }
            public HashSet<string> ExistingAttachmentIds { get; set; }
            public Dictionary<string, List<Attachment>> ExistingAttachmentsByNote { get; set; }
            public Dictionary<string, string> AttachmentIdByPath { get; set; }
            public HashSet<string> ReferencedPaths { get; set; }
            public List<ImportedAttachment> Attachments { get; set; }
        }

        public static ImportPlan Build(ImportSource source)
        {
            Context context = new Context
            {
                Warnings = new List<string>(),
                DroppedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                OthersByPath = new Dictionary<string, ImportFile>(StringComparer.OrdinalIgnoreCase),
                NoteIdByMdPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                MdPathsByStem = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                OtherPathsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                ResourcePathByStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ValidNoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ExistingNoteIdsByTitle = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                ExistingAttachmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ExistingAttachmentsByNote = new Dictionary<string, List<Attachment>>(StringComparer.OrdinalIgnoreCase),
                AttachmentIdByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                Attachments = new List<ImportedAttachment>(),
            };

            // A Joplin RAW export is rewritten into the canonical tree shape first;
            // any other input passes through untouched.
            JoplinRawTransform.Result raw = JoplinRawTransform.Apply(source?.Files);
            context.Warnings.AddRange(raw.Warnings);

            Dictionary<string, ImportFile> markdownByPath = CollectFiles(raw.Files, context);
            bool templatesSection = DetectTemplatesSection(markdownByPath);
            List<Node> ordered = BuildNodes(markdownByPath, templatesSection, context);

            Dictionary<string, Note> existingById = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
            foreach (Note note in source?.ExistingNotes ?? new List<Note>())
            {
                if (note != null && !string.IsNullOrEmpty(note.Id))
                {
                    existingById[note.Id] = note;
                    context.ValidNoteIds.Add(note.Id);
                    if (!note.Deleted)
                    {
                        AddToList(context.ExistingNoteIdsByTitle, note.Title ?? string.Empty, note.Id);
                    }
                }
            }

            foreach (Attachment attachment in source?.ExistingAttachments ?? new List<Attachment>())
            {
                if (attachment != null && !attachment.Deleted && !string.IsNullOrEmpty(attachment.Id))
                {
                    context.ExistingAttachmentIds.Add(attachment.Id);
                    if (!string.IsNullOrEmpty(attachment.NoteId))
                    {
                        if (!context.ExistingAttachmentsByNote.ContainsKey(attachment.NoteId))
                        {
                            context.ExistingAttachmentsByNote[attachment.NoteId] = new List<Attachment>();
                        }
                        context.ExistingAttachmentsByNote[attachment.NoteId].Add(attachment);
                    }
                }
            }

            AssignIds(ordered, existingById, BuildScope(source?.DestinationParentId, existingById), context);

            foreach (Node node in ordered)
            {
                if (node.File != null)
                {
                    context.NoteIdByMdPath[node.File.Path] = node.FinalId;
                    AddToList(context.MdPathsByStem, node.Name, node.File.Path);
                }
            }

            Dictionary<string, Node> nodeByKey = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            foreach (Node node in ordered)
            {
                nodeByKey[node.Key] = node;
            }

            List<ImportedNote> notes = new List<ImportedNote>();
            foreach (Node node in ordered)
            {
                notes.Add(BuildNote(node, nodeByKey, source, context));
            }

            foreach (KeyValuePair<string, ImportFile> other in context.OthersByPath)
            {
                if (!context.ReferencedPaths.Contains(other.Key))
                {
                    context.Warnings.Add("Skipped unreferenced file \"" + other.Value.Path + "\".");
                }
            }

            foreach (KeyValuePair<string, int> dropped in context.DroppedKeys)
            {
                string files = dropped.Value == 1 ? "1 file" : dropped.Value.ToString(CultureInfo.InvariantCulture) + " files";
                context.Warnings.Add("Dropped front matter key \"" + dropped.Key + "\" from " + files + ".");
            }

            return new ImportPlan
            {
                Notes = notes,
                Attachments = context.Attachments,
                Warnings = context.Warnings,
            };
        }

        private static Dictionary<string, ImportFile> CollectFiles(List<ImportFile> files, Context context)
        {
            Dictionary<string, ImportFile> markdown = new Dictionary<string, ImportFile>(StringComparer.OrdinalIgnoreCase);

            foreach (ImportFile file in files ?? new List<ImportFile>())
            {
                string path = (file?.Path ?? string.Empty).Replace('\\', '/').Trim('/');
                if (path.Length > 0 && !HasDotSegment(path))
                {
                    file.Path = path;
                    if (path.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        markdown[path] = file;
                    }
                    else
                    {
                        context.OthersByPath[path] = file;
                        AddToList(context.OtherPathsByName, LastSegment(path), path);

                        string[] segments = path.Split('/');
                        if (segments.Length == 2 && string.Equals(segments[0], JoplinResourcesDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            context.ResourcePathByStem[Stem(segments[1])] = path;
                        }
                    }
                }
            }

            return markdown;
        }

        // Dot-directories are tool internals (.obsidian, .git, .trash), never notes.
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

        // The exact inverse of what the export writes: a root Templates/ folder with
        // no sibling Templates.md is the template section, not a note
        // (features/import.md).
        private static bool DetectTemplatesSection(Dictionary<string, ImportFile> markdownByPath)
        {
            bool hasTemplatesContent = false;
            bool hasTemplatesNote = false;

            foreach (string path in markdownByPath.Keys)
            {
                int slash = path.IndexOf('/');
                if (slash < 0)
                {
                    if (string.Equals(Stem(path), MarkdownExportBuilder.TemplatesDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        hasTemplatesNote = true;
                    }
                }
                else if (string.Equals(path.Substring(0, slash), MarkdownExportBuilder.TemplatesDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    hasTemplatesContent = true;
                }
            }

            return hasTemplatesContent && !hasTemplatesNote;
        }

        private static List<Node> BuildNodes(
            Dictionary<string, ImportFile> markdownByPath,
            bool templatesSection,
            Context context)
        {
            Dictionary<string, Node> nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ImportFile> entry in markdownByPath)
            {
                string key = entry.Key.Substring(0, entry.Key.Length - NoteExtension.Length);
                Node node = MakeNode(key, templatesSection, context);
                node.File = entry.Value;
                node.FrontMatter = ImportFrontMatter.Parse(entry.Value.Content ?? string.Empty);
                nodes[key] = node;
            }

            // A folder with no matching .md (normal in Obsidian vaults) becomes an
            // empty note - but only folders that lead to markdown; attachments/ and
            // _resources/ hold referenced files, not notes.
            foreach (string path in new List<string>(markdownByPath.Keys))
            {
                string directory = DirectoryOf(path);
                while (directory.Length > 0)
                {
                    bool isSectionRoot = templatesSection
                        && directory.IndexOf('/') < 0
                        && string.Equals(directory, MarkdownExportBuilder.TemplatesDirectory, StringComparison.OrdinalIgnoreCase);
                    if (!isSectionRoot && !nodes.ContainsKey(directory))
                    {
                        nodes[directory] = MakeNode(directory, templatesSection, context);
                    }
                    directory = DirectoryOf(directory);
                }
            }

            Dictionary<string, List<Node>> childrenByParent = new Dictionary<string, List<Node>>(StringComparer.OrdinalIgnoreCase);
            foreach (Node node in nodes.Values)
            {
                if (!childrenByParent.ContainsKey(node.ParentKey))
                {
                    childrenByParent[node.ParentKey] = new List<Node>();
                }
                childrenByParent[node.ParentKey].Add(node);
            }

            List<Node> ordered = new List<Node>();
            EmitChildren(string.Empty, childrenByParent, ordered, includeTemplates: false);
            if (templatesSection)
            {
                EmitChildren(MarkdownExportBuilder.TemplatesDirectory, childrenByParent, ordered, includeTemplates: true);
            }
            return ordered;
        }

        private static Node MakeNode(string key, bool templatesSection, Context context)
        {
            string name = LastSegment(key);
            int slash = key.IndexOf('/');
            bool inTemplates = templatesSection && slash >= 0
                && string.Equals(key.Substring(0, slash), MarkdownExportBuilder.TemplatesDirectory, StringComparison.OrdinalIgnoreCase);
            return new Node
            {
                Key = key,
                ParentKey = DirectoryOf(key),
                Name = name,
                InTemplates = inTemplates,
            };
        }

        private static void EmitChildren(
            string parentKey,
            Dictionary<string, List<Node>> childrenByParent,
            List<Node> ordered,
            bool includeTemplates)
        {
            List<Node> children;
            if (childrenByParent.TryGetValue(parentKey, out children))
            {
                children.Sort(CompareNodes);
                foreach (Node child in children)
                {
                    // At the top level the template section is emitted in its own
                    // pass, mirroring the export's layout order.
                    if (parentKey.Length > 0 || child.InTemplates == includeTemplates)
                    {
                        ordered.Add(child);
                        EmitChildren(child.Key, childrenByParent, ordered, includeTemplates);
                    }
                }
            }
        }

        private static int CompareNodes(Node left, Node right)
        {
            int result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            if (result == 0)
            {
                result = string.CompareOrdinal(left.Name, right.Name);
            }
            return result;
        }

        // The merge check is scoped to the chosen destination: "import into X" means
        // the result lives under X, so a match elsewhere in the tree (or in the
        // trash) is a copy with a fresh id, not an in-place update. Root scopes to
        // the whole tree; template-section files ignore the destination and match
        // globally (features/import.md).
        private static HashSet<string> BuildScope(string destinationParentId, Dictionary<string, Note> existingById)
        {
            HashSet<string> scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(destinationParentId))
            {
                foreach (Note note in existingById.Values)
                {
                    if (!note.Deleted)
                    {
                        scope.Add(note.Id);
                    }
                }
            }
            else
            {
                Dictionary<string, List<string>> childrenByParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (Note note in existingById.Values)
                {
                    if (!note.Deleted && !string.IsNullOrEmpty(note.ParentId))
                    {
                        AddToList(childrenByParent, note.ParentId, note.Id);
                    }
                }

                Note destination;
                if (existingById.TryGetValue(destinationParentId, out destination) && !destination.Deleted)
                {
                    Queue<string> pending = new Queue<string>();
                    pending.Enqueue(destination.Id);
                    while (pending.Count > 0)
                    {
                        string id = pending.Dequeue();
                        if (scope.Add(id))
                        {
                            List<string> children;
                            if (childrenByParent.TryGetValue(id, out children))
                            {
                                foreach (string child in children)
                                {
                                    pending.Enqueue(child);
                                }
                            }
                        }
                    }
                }
            }

            return scope;
        }

        private static void AssignIds(List<Node> ordered, Dictionary<string, Note> existingById, HashSet<string> scope, Context context)
        {
            HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Node node in ordered)
            {
                string fmId = node.FrontMatter?.LoresteadId;
                string finalId = null;
                Guid parsed;

                if (!string.IsNullOrEmpty(fmId))
                {
                    if (Guid.TryParse(fmId, out parsed))
                    {
                        node.HadId = true;
                        if (claimed.Contains(fmId))
                        {
                            // Already claimed by an earlier file in this import.
                            context.Warnings.Add("Duplicate lorestead-id in \"" + node.File.Path + "\"; imported as a new note.");
                        }
                        else if (existingById.TryGetValue(fmId, out Note existing))
                        {
                            // Out of scope (or trashed) falls through to a fresh id:
                            // an ordinary copy, not a warning - the preflight is what
                            // says which notes will merge.
                            if (!existing.Deleted && (node.InTemplates || scope.Contains(fmId)))
                            {
                                finalId = fmId;
                                node.Existing = existing;
                            }
                        }
                        else
                        {
                            // Unknown id is kept: export, fresh install, import,
                            // re-import stays idempotent.
                            finalId = fmId;
                        }
                    }
                    else
                    {
                        context.Warnings.Add("Ignored invalid lorestead-id in \"" + node.File.Path + "\".");
                    }
                }

                node.FinalId = finalId ?? Guid.CreateVersion7().ToString();
                claimed.Add(node.FinalId);
                context.ValidNoteIds.Add(node.FinalId);
            }
        }

        private static ImportedNote BuildNote(Node node, Dictionary<string, Node> nodeByKey, ImportSource source, Context context)
        {
            string title = node.FrontMatter?.Title;
            if (string.IsNullOrEmpty(title))
            {
                title = node.Name;
            }
            node.Title = NoteTitle.Normalize(title);

            string body = string.Empty;
            string createdAt = null;
            string updatedAt = null;

            if (node.File != null)
            {
                body = RewriteLinks(node.FrontMatter.Body, node, context);
                createdAt = ParseDate(node.FrontMatter.Created, node, context);
                updatedAt = ParseDate(node.FrontMatter.Updated, node, context);

                foreach (string key in node.FrontMatter.UnknownKeys)
                {
                    int count;
                    context.DroppedKeys.TryGetValue(key, out count);
                    context.DroppedKeys[key] = count + 1;
                }
            }

            ImportedNote result = new ImportedNote
            {
                Id = node.FinalId,
                Title = node.Title,
                Body = body,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                HadFrontMatterId = node.HadId,
                Path = node.File?.Path ?? node.Key,
            };

            if (node.Existing != null)
            {
                result.Action = string.Equals(node.Existing.Title, node.Title, StringComparison.Ordinal)
                    && string.Equals(node.Existing.Body, body, StringComparison.Ordinal)
                    ? ImportAction.SkipIdentical
                    : ImportAction.Merge;
                result.Type = node.Existing.Type;
            }
            else
            {
                result.Action = ImportAction.Create;
                bool templateRoot = node.InTemplates && node.ParentKey.IndexOf('/') < 0;
                result.Type = templateRoot ? NoteType.Template : NoteType.Normal;

                Node parent;
                if (node.ParentKey.Length > 0 && nodeByKey.TryGetValue(node.ParentKey, out parent))
                {
                    result.ParentId = parent.FinalId;
                }
                else if (!node.InTemplates)
                {
                    result.ParentId = source?.DestinationParentId;
                }
            }

            return result;
        }

        private static string RewriteLinks(string body, Node node, Context context)
        {
            string result = body ?? string.Empty;
            string directory = DirectoryOf(node.File.Path);

            if (result.Length > 0)
            {
                result = MarkdownLinkPattern().Replace(result, delegate (Match match)
                {
                    string inside = match.Groups[1].Value;
                    string url = inside;
                    string suffix = string.Empty;

                    // An optional markdown link title: ](path "tooltip").
                    int quote = inside.LastIndexOf(" \"", StringComparison.Ordinal);
                    if (quote > 0 && inside.EndsWith("\"", StringComparison.Ordinal))
                    {
                        url = inside.Substring(0, quote);
                        suffix = inside.Substring(quote);
                    }

                    string rewritten = RewriteUrl(url.Trim(), directory, node, context);
                    return rewritten == null ? match.Value : "](" + rewritten + suffix + ")";
                });

                result = WikiLinkPattern().Replace(result, delegate (Match match)
                {
                    return RewriteWikiLink(match, node, context);
                });
            }

            return result;
        }

        private static string RewriteUrl(string url, string directory, Node node, Context context)
        {
            string result = null;

            if (url.Length > 0)
            {
                Match joplin = JoplinLinkPattern().Match(url);
                if (url.StartsWith("note://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!context.ValidNoteIds.Contains(url.Substring("note://".Length)))
                    {
                        context.Warnings.Add("Unresolved link \"" + url + "\" in \"" + node.File.Path + "\".");
                    }
                }
                else if (url.StartsWith("attachment://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!context.ExistingAttachmentIds.Contains(url.Substring("attachment://".Length)))
                    {
                        context.Warnings.Add("Unresolved link \"" + url + "\" in \"" + node.File.Path + "\".");
                    }
                }
                else if (joplin.Success)
                {
                    // Joplin's export leaves resource links in its internal :/<id>
                    // form; the id doubles as the _resources filename. A miss is a
                    // Joplin note link, which nothing in the export can map.
                    string resourcePath;
                    if (context.ResourcePathByStem.TryGetValue(joplin.Groups[1].Value, out resourcePath))
                    {
                        string id = ResolveAttachment(resourcePath, node, context);
                        result = id == null ? null : "attachment://" + id;
                    }
                    else
                    {
                        context.Warnings.Add("Joplin internal link \"" + url + "\" in \"" + node.File.Path + "\" was left as-is.");
                    }
                }
                else if (!SchemePattern().IsMatch(url)
                    && !url.StartsWith("#", StringComparison.Ordinal)
                    && !url.StartsWith("//", StringComparison.Ordinal))
                {
                    result = RewriteRelative(url, directory, node, context);
                }
            }

            return result;
        }

        private static string RewriteRelative(string url, string directory, Node node, Context context)
        {
            string result = null;
            string decoded = Uri.UnescapeDataString(url);

            int hash = decoded.IndexOf('#');
            if (hash >= 0)
            {
                // Native links have no anchor form; the note is the closest target.
                decoded = decoded.Substring(0, hash);
            }

            if (decoded.Length > 0)
            {
                string full = ResolvePath(directory, decoded);
                string noteId;
                if (full == null)
                {
                    context.Warnings.Add("Unresolved link \"" + url + "\" in \"" + node.File.Path + "\".");
                }
                else if (context.NoteIdByMdPath.TryGetValue(full, out noteId))
                {
                    result = "note://" + noteId;
                }
                else if (context.OthersByPath.ContainsKey(full))
                {
                    string id = ResolveAttachment(full, node, context);
                    result = id == null ? null : "attachment://" + id;
                }
                else
                {
                    context.Warnings.Add("Unresolved link \"" + url + "\" in \"" + node.File.Path + "\".");
                }
            }

            return result;
        }

        private static string RewriteWikiLink(Match match, Node node, Context context)
        {
            string result = match.Value;
            bool embed = match.Groups[1].Value.Length > 0;
            string inside = match.Groups[2].Value;

            string target = inside;
            string alias = null;
            int pipe = inside.IndexOf('|');
            if (pipe >= 0)
            {
                target = inside.Substring(0, pipe);
                alias = inside.Substring(pipe + 1).Trim();
            }

            int hash = target.IndexOf('#');
            string display = alias ?? target.Trim();
            if (hash >= 0)
            {
                target = target.Substring(0, hash);
            }
            target = target.Trim();

            if (target.Length > 0)
            {
                string mdPath = null;
                string filePath = null;

                if (target.IndexOf('/') >= 0)
                {
                    string candidate = target.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase)
                        ? target
                        : target + NoteExtension;
                    if (context.NoteIdByMdPath.ContainsKey(candidate))
                    {
                        mdPath = candidate;
                    }
                    else if (context.OthersByPath.ContainsKey(target))
                    {
                        filePath = target;
                    }
                }
                else if (embed && context.OtherPathsByName.ContainsKey(target))
                {
                    filePath = PickShortest(context.OtherPathsByName[target], match.Value, node, context);
                }
                else
                {
                    string stem = target.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase)
                        ? target.Substring(0, target.Length - NoteExtension.Length)
                        : target;
                    List<string> mdCandidates;
                    if (context.MdPathsByStem.TryGetValue(stem, out mdCandidates))
                    {
                        mdPath = PickShortest(mdCandidates, match.Value, node, context);
                    }
                    else if (context.OtherPathsByName.ContainsKey(target))
                    {
                        filePath = PickShortest(context.OtherPathsByName[target], match.Value, node, context);
                    }
                }

                if (mdPath != null)
                {
                    // An embedded note has no native equivalent, so it becomes a
                    // plain link.
                    result = "[" + display + "](note://" + context.NoteIdByMdPath[mdPath] + ")";
                }
                else if (filePath != null)
                {
                    string id = ResolveAttachment(filePath, node, context);
                    if (id != null)
                    {
                        result = (embed ? "!" : string.Empty) + "[" + display + "](attachment://" + id + ")";
                    }
                }
                else
                {
                    // A miss in the import set falls back to existing notes by title,
                    // so a wikilink to a note that is already in the database resolves
                    // instead of importing as a dead link. Unique matches only -
                    // titles can legally collide, and guessing links the wrong note.
                    List<string> existingIds;
                    if (context.ExistingNoteIdsByTitle.TryGetValue(target, out existingIds))
                    {
                        if (existingIds.Count == 1)
                        {
                            result = "[" + display + "](note://" + existingIds[0] + ")";
                        }
                        else
                        {
                            context.Warnings.Add("Ambiguous link \"" + match.Value + "\" in \"" + node.File.Path
                                + "\": " + existingIds.Count.ToString(CultureInfo.InvariantCulture)
                                + " existing notes share that title.");
                        }
                    }
                    else
                    {
                        context.Warnings.Add("Unresolved link \"" + match.Value + "\" in \"" + node.File.Path + "\".");
                    }
                }
            }

            return result;
        }

        // Obsidian resolves an ambiguous name to the shortest path; matching that
        // beats inventing a different rule.
        private static string PickShortest(List<string> candidates, string link, Node node, Context context)
        {
            List<string> sorted = new List<string>(candidates);
            sorted.Sort(delegate (string left, string right)
            {
                int result = left.Length.CompareTo(right.Length);
                if (result == 0)
                {
                    result = string.CompareOrdinal(left, right);
                }
                return result;
            });

            if (sorted.Count > 1)
            {
                context.Warnings.Add("Ambiguous link \"" + link + "\" in \"" + node.File.Path + "\"; using \"" + sorted[0] + "\".");
            }

            return sorted[0];
        }

        private static string ResolveAttachment(string path, Node node, Context context)
        {
            string result;
            context.ReferencedPaths.Add(path);

            if (!context.AttachmentIdByPath.TryGetValue(path, out result))
            {
                ImportFile file = context.OthersByPath[path];
                if (file.SizeBytes > MaxAttachmentBytes)
                {
                    context.Warnings.Add("Skipped attachment \"" + file.Path + "\": over the 100 MB size limit.");
                }
                else
                {
                    result = FindReusableAttachment(file, node, context);
                    if (result == null)
                    {
                        ImportedAttachment attachment = new ImportedAttachment
                        {
                            Id = Guid.CreateVersion7().ToString(),
                            NoteId = node.FinalId,
                            SourcePath = string.IsNullOrEmpty(file.SourcePath) ? file.Path : file.SourcePath,
                            Filename = LastSegment(file.Path),
                            MimeType = MimeFromExtension(file.Path),
                            SizeBytes = file.SizeBytes,
                        };
                        context.Attachments.Add(attachment);
                        result = attachment.Id;
                    }
                    context.AttachmentIdByPath[path] = result;
                }
            }

            return result;
        }

        // A merged note reuses an attachment it already owns when filename and size
        // match - without this, every re-import of an unchanged export would
        // duplicate every blob (features/import.md).
        private static string FindReusableAttachment(ImportFile file, Node node, Context context)
        {
            string result = null;

            if (node.Existing != null)
            {
                string name = LastSegment(file.Path);
                string stripped = CopySuffixPattern().Replace(Stem(name), string.Empty) + Extension(name);
                List<Attachment> owned;
                if (context.ExistingAttachmentsByNote.TryGetValue(node.Existing.Id, out owned))
                {
                    foreach (Attachment attachment in owned)
                    {
                        if (result == null && attachment.SizeBytes == file.SizeBytes)
                        {
                            string sanitized = ExportFileName.SanitizeAttachment(attachment.Filename);
                            if (string.Equals(sanitized, name, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(sanitized, stripped, StringComparison.OrdinalIgnoreCase))
                            {
                                result = attachment.Id;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static string ParseDate(string value, Node node, Context context)
        {
            string result = null;
            DateTime parsed;

            if (!string.IsNullOrEmpty(value))
            {
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                {
                    result = parsed.ToString("O", CultureInfo.InvariantCulture);
                }
                else
                {
                    context.Warnings.Add("Unreadable date \"" + value + "\" in \"" + node.File.Path + "\"; using the import time.");
                }
            }

            return result;
        }

        private static string ResolvePath(string directory, string relative)
        {
            string combined = directory.Length == 0 ? relative : directory + "/" + relative;
            List<string> resolved = new List<string>();
            bool escaped = false;

            foreach (string segment in combined.Replace('\\', '/').Split('/'))
            {
                if (segment == "..")
                {
                    if (resolved.Count == 0)
                    {
                        escaped = true;
                    }
                    else
                    {
                        resolved.RemoveAt(resolved.Count - 1);
                    }
                }
                else if (segment.Length > 0 && segment != ".")
                {
                    resolved.Add(segment);
                }
            }

            return escaped ? null : string.Join("/", resolved);
        }

        private static string MimeFromExtension(string path)
        {
            string result;
            switch (Extension(path).ToLowerInvariant())
            {
                case ".png": result = "image/png"; break;
                case ".jpg":
                case ".jpeg": result = "image/jpeg"; break;
                case ".gif": result = "image/gif"; break;
                case ".webp": result = "image/webp"; break;
                case ".svg": result = "image/svg+xml"; break;
                case ".bmp": result = "image/bmp"; break;
                case ".pdf": result = "application/pdf"; break;
                case ".txt": result = "text/plain"; break;
                case ".csv": result = "text/csv"; break;
                case ".json": result = "application/json"; break;
                case ".xml": result = "application/xml"; break;
                case ".html": result = "text/html"; break;
                case ".zip": result = "application/zip"; break;
                case ".mp3": result = "audio/mpeg"; break;
                case ".wav": result = "audio/wav"; break;
                case ".mp4": result = "video/mp4"; break;
                case ".webm": result = "video/webm"; break;
                default: result = "application/octet-stream"; break;
            }
            return result;
        }

        private static void AddToList(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.ContainsKey(key))
            {
                map[key] = new List<string>();
            }
            map[key].Add(value);
        }

        private static string DirectoryOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        private static string LastSegment(string path)
        {
            int slash = path.LastIndexOf('/');
            string name = slash < 0 ? path : path.Substring(slash + 1);
            return name.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - NoteExtension.Length)
                : name;
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
    }
}
