using System.Collections.Generic;
using Lorestead.Core.Entities;
using Lorestead.Core.Import;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class MarkdownImportTests
    {
        private const string ParentId = "0198d0de-0000-7000-8000-000000000001";
        private const string ChildId = "0198d0de-0000-7000-8000-000000000002";
        private const string UnknownId = "0198d0de-0000-7000-8000-0000000000aa";
        private const string AttachmentId = "0198d0de-1111-7000-8000-000000000001";
        private const string DestinationId = "0198d0de-2222-7000-8000-000000000001";

        [Fact]
        public void ReimportingAnUnchangedExportSkipsEverything()
        {
            Note parent = MakeExisting(ParentId, "Parent", "Down to [Child](note://" + ChildId + ").");
            Note child = MakeExisting(ChildId, "Child", "Hello.");

            ImportPlan plan = Build(
                new List<Note> { parent, child },
                MakeFile("Parent.md", Fenced(ParentId, "Parent", "Down to [Child](Parent/Child.md).")),
                MakeFile("Parent/Child.md", Fenced(ChildId, "Child", "Hello.")));

            Assert.Equal(2, plan.Notes.Count);
            Assert.All(plan.Notes, note => Assert.Equal(ImportAction.SkipIdentical, note.Action));
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void AChangedFileMergesAndTheFileWins()
        {
            Note parent = MakeExisting(ParentId, "Parent", "Old body.");

            ImportPlan plan = Build(
                new List<Note> { parent },
                MakeFile("Parent.md", Fenced(ParentId, "Parent", "New body.")));

            ImportedNote merged = NoteAt(plan, "Parent.md");
            Assert.Equal(ImportAction.Merge, merged.Action);
            Assert.Equal("New body.", merged.Body);
            Assert.Null(merged.ParentId);
        }

        [Fact]
        public void AFolderWithoutAMatchingNoteBecomesAnEmptyNote()
        {
            ImportPlan plan = Build(null, MakeFile("Ideas/Note.md", "Body."));

            ImportedNote folder = NoteAt(plan, "Ideas");
            ImportedNote note = NoteAt(plan, "Ideas/Note.md");
            Assert.Equal("Ideas", folder.Title);
            Assert.Equal(string.Empty, folder.Body);
            Assert.Equal(folder.Id, note.ParentId);
        }

        [Fact]
        public void FrontMatterTitleWinsOverTheFilenameAndFallsBackToIt()
        {
            ImportPlan plan = Build(null,
                MakeFile("Q3 Roadmap.md", "---\ntitle: \"Q3: Roadmap\"\n---\n\nBody."),
                MakeFile("Plain.md", "No front matter."));

            Assert.Equal("Q3: Roadmap", NoteAt(plan, "Q3 Roadmap.md").Title);
            Assert.Equal("Plain", NoteAt(plan, "Plain.md").Title);
        }

        [Fact]
        public void AnUnknownIdIsKeptSoReimportIntoAFreshDatabaseStaysIdempotent()
        {
            ImportPlan plan = Build(null, MakeFile("Note.md", Fenced(UnknownId, "Note", "Body.")));

            ImportedNote note = NoteAt(plan, "Note.md");
            Assert.Equal(ImportAction.Create, note.Action);
            Assert.Equal(UnknownId, note.Id);
            Assert.True(note.HadFrontMatterId);
        }

        [Fact]
        public void ATrashedMatchBecomesAnOrdinaryCopy()
        {
            Note trashed = MakeExisting(ParentId, "Gone", "Body.");
            trashed.Deleted = true;

            ImportPlan plan = Build(new List<Note> { trashed },
                MakeFile("Gone.md", Fenced(ParentId, "Gone", "Body.")));

            ImportedNote note = NoteAt(plan, "Gone.md");
            Assert.Equal(ImportAction.Create, note.Action);
            Assert.NotEqual(ParentId, note.Id);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void ADuplicateIdInTheSetImportsTheLaterFileAsANewNote()
        {
            ImportPlan plan = Build(null,
                MakeFile("A.md", Fenced(UnknownId, "A", "Body.")),
                MakeFile("B.md", Fenced(UnknownId, "B", "Body.")));

            Assert.Equal(UnknownId, NoteAt(plan, "A.md").Id);
            Assert.NotEqual(UnknownId, NoteAt(plan, "B.md").Id);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Duplicate lorestead-id"));
        }

        [Fact]
        public void ARelativeLinkBecomesANoteLinkAndAnUnresolvedOneIsLeftAlone()
        {
            ImportPlan plan = Build(null,
                MakeFile("Parent.md", "See [Child](Parent/Child.md) and [Gone](Missing.md)."),
                MakeFile("Parent/Child.md", "Up to [Parent](../Parent.md)."));

            ImportedNote parent = NoteAt(plan, "Parent.md");
            ImportedNote child = NoteAt(plan, "Parent/Child.md");
            Assert.Contains("[Child](note://" + child.Id + ")", parent.Body);
            Assert.Contains("[Parent](note://" + parent.Id + ")", child.Body);
            Assert.Contains("[Gone](Missing.md)", parent.Body);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Missing.md"));
        }

        [Fact]
        public void AReferencedFileBecomesAnAttachmentAndAnUnreferencedOneIsSkipped()
        {
            ImportPlan plan = Build(null,
                MakeFile("Note.md", "![diagram](attachments/my%20diagram.png)"),
                MakeBinary("attachments/my diagram.png", 10),
                MakeBinary("attachments/orphan.png", 10));

            ImportedNote note = NoteAt(plan, "Note.md");
            ImportedAttachment attachment = Assert.Single(plan.Attachments);
            Assert.Equal(note.Id, attachment.NoteId);
            Assert.Equal("my diagram.png", attachment.Filename);
            Assert.Equal("image/png", attachment.MimeType);
            Assert.Contains("![diagram](attachment://" + attachment.Id + ")", note.Body);
            Assert.Contains(plan.Warnings, warning => warning.Contains("orphan.png"));
        }

        [Fact]
        public void WikilinksResolveByNameWithAliasesAndEmbeds()
        {
            ImportPlan plan = Build(null,
                MakeFile("Note.md", "See [[Target]] and [[Target|the target]] and ![[img.png]]."),
                MakeFile("Sub/Target.md", "Body."),
                MakeBinary("img.png", 5));

            ImportedNote note = NoteAt(plan, "Note.md");
            ImportedNote target = NoteAt(plan, "Sub/Target.md");
            ImportedAttachment image = Assert.Single(plan.Attachments);
            Assert.Contains("[Target](note://" + target.Id + ")", note.Body);
            Assert.Contains("[the target](note://" + target.Id + ")", note.Body);
            Assert.Contains("![img.png](attachment://" + image.Id + ")", note.Body);
        }

        // A wikilink with no match in the import set falls back to existing notes
        // by title - unique matches only, since titles can legally collide.
        [Fact]
        public void AWikilinkFallsBackToExistingNoteTitles()
        {
            Note existing = MakeExisting(ParentId, "Naming rounds", "Body.");
            Note twinA = MakeExisting(ChildId, "Twin", "Body.");
            Note twinB = MakeExisting(UnknownId, "Twin", "Body.");

            ImportPlan plan = Build(new List<Note> { existing, twinA, twinB },
                MakeFile("Note.md", "See [[Naming rounds]] and [[Twin]] and [[Nowhere]]."));

            ImportedNote note = NoteAt(plan, "Note.md");
            Assert.Contains("[Naming rounds](note://" + ParentId + ")", note.Body);
            Assert.Contains("[[Twin]]", note.Body);
            Assert.Contains("[[Nowhere]]", note.Body);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Ambiguous") && warning.Contains("[[Twin]]"));
            Assert.Contains(plan.Warnings, warning => warning.Contains("[[Nowhere]]"));
        }

        [Fact]
        public void AnAmbiguousWikilinkPicksTheShortestPath()
        {
            ImportPlan plan = Build(null,
                MakeFile("Note.md", "See [[Target]]."),
                MakeFile("A/Target.md", "Near."),
                MakeFile("A/Deep/Target.md", "Far."));

            ImportedNote near = NoteAt(plan, "A/Target.md");
            Assert.Contains("note://" + near.Id, NoteAt(plan, "Note.md").Body);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Ambiguous"));
        }

        [Fact]
        public void JoplinResourceLinksResolveThroughTheResourcesFolder()
        {
            string resourceId = "0123456789abcdef0123456789abcdef";
            string noteLinkId = "ffffffffffffffffffffffffffffffff";

            ImportPlan plan = Build(null,
                MakeFile("Note.md", "![pic](:/" + resourceId + ") and [other](:/" + noteLinkId + ")."),
                MakeBinary("_resources/" + resourceId + ".png", 5));

            ImportedAttachment attachment = Assert.Single(plan.Attachments);
            Assert.Contains("![pic](attachment://" + attachment.Id + ")", NoteAt(plan, "Note.md").Body);
            Assert.Contains("[other](:/" + noteLinkId + ")", NoteAt(plan, "Note.md").Body);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Joplin internal link"));
        }

        [Fact]
        public void ATemplatesFolderImportsAsTemplateRoots()
        {
            ImportPlan plan = Build(null,
                MakeFile("Templates/Project.md", "Body."),
                MakeFile("Templates/Project/Overview.md", "Body."));

            ImportedNote root = NoteAt(plan, "Templates/Project.md");
            ImportedNote child = NoteAt(plan, "Templates/Project/Overview.md");
            Assert.Equal(NoteType.Template, root.Type);
            Assert.Null(root.ParentId);
            Assert.Equal(NoteType.Normal, child.Type);
            Assert.Equal(root.Id, child.ParentId);
        }

        [Fact]
        public void ASiblingTemplatesNoteDefeatsTheSectionDetection()
        {
            ImportPlan plan = Build(null,
                MakeFile("Templates.md", "A real note."),
                MakeFile("Templates/Inside.md", "Body."));

            Assert.All(plan.Notes, note => Assert.Equal(NoteType.Normal, note.Type));
            Assert.Equal(NoteAt(plan, "Templates.md").Id, NoteAt(plan, "Templates/Inside.md").ParentId);
        }

        [Fact]
        public void DotDirectoriesAreSkippedEntirely()
        {
            ImportPlan plan = Build(null,
                MakeFile("Note.md", "Body."),
                MakeFile(".obsidian/workspace.json", "{}"),
                MakeFile(".trash/Deleted.md", "Body."));

            Assert.Single(plan.Notes);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void UnknownFrontMatterKeysAreDroppedAndCounted()
        {
            ImportPlan plan = Build(null,
                MakeFile("A.md", "---\ntitle: A\ntags: x\n---\n\nBody."),
                MakeFile("B.md", "---\ntitle: B\ntags: y\n---\n\nBody."));

            Assert.DoesNotContain("tags", NoteAt(plan, "A.md").Body);
            Assert.Contains(plan.Warnings, warning => warning.Contains("\"tags\"") && warning.Contains("2 files"));
        }

        [Fact]
        public void AMergedNoteReusesItsExistingAttachmentByNameAndSize()
        {
            Note existing = MakeExisting(ParentId, "Note", "![diagram](attachment://" + AttachmentId + ")");
            Attachment owned = new Attachment
            {
                Id = AttachmentId,
                NoteId = ParentId,
                Filename = "diagram.png",
                SizeBytes = 10,
            };

            ImportPlan plan = MarkdownImportBuilder.Build(new ImportSource
            {
                Files = new List<ImportFile>
                {
                    MakeFile("Note.md", Fenced(ParentId, "Note", "![diagram](attachments/diagram.png)")),
                    MakeBinary("attachments/diagram.png", 10),
                },
                ExistingNotes = new List<Note> { existing },
                ExistingAttachments = new List<Attachment> { owned },
            });

            Assert.Empty(plan.Attachments);
            Assert.Equal(ImportAction.SkipIdentical, NoteAt(plan, "Note.md").Action);
        }

        // The merge check follows the destination: a match inside its subtree
        // updates in place, a match elsewhere in the tree becomes a copy under it.
        [Fact]
        public void TheMergeScopeFollowsTheDestination()
        {
            Note destination = MakeExisting(DestinationId, "Target", "");
            Note inside = MakeExisting(ParentId, "Inside", "Old.");
            inside.ParentId = DestinationId;
            Note elsewhere = MakeExisting(ChildId, "Elsewhere", "Old.");
            List<Note> existing = new List<Note> { destination, inside, elsewhere };

            ImportPlan plan = MarkdownImportBuilder.Build(new ImportSource
            {
                Files = new List<ImportFile>
                {
                    MakeFile("Inside.md", Fenced(ParentId, "Inside", "New.")),
                    MakeFile("Elsewhere.md", Fenced(ChildId, "Elsewhere", "New.")),
                    MakeFile("New.md", "Body."),
                },
                ExistingNotes = existing,
                DestinationParentId = DestinationId,
            });

            ImportedNote merged = NoteAt(plan, "Inside.md");
            Assert.Equal(ImportAction.Merge, merged.Action);
            Assert.Equal(ParentId, merged.Id);
            Assert.Null(merged.ParentId);

            ImportedNote copy = NoteAt(plan, "Elsewhere.md");
            Assert.Equal(ImportAction.Create, copy.Action);
            Assert.NotEqual(ChildId, copy.Id);
            Assert.Equal(DestinationId, copy.ParentId);

            Assert.Equal(DestinationId, NoteAt(plan, "New.md").ParentId);
        }

        [Fact]
        public void ARootImportMergesWithAMatchAnywhereInTheTree()
        {
            Note nested = MakeExisting(ParentId, "Nested", "Old.");
            nested.ParentId = ChildId;
            Note parent = MakeExisting(ChildId, "Parent", "");

            ImportPlan plan = Build(new List<Note> { parent, nested },
                MakeFile("Nested.md", Fenced(ParentId, "Nested", "New.")));

            Assert.Equal(ImportAction.Merge, NoteAt(plan, "Nested.md").Action);
        }

        [Fact]
        public void FrontMatterDatesAreParsedFromJoplinsSpelling()
        {
            ImportPlan plan = Build(null,
                MakeFile("Note.md", "---\ntitle: Note\ncreated: 2026-07-25 14:02:11Z\nupdated: not a date\n---\n\nBody."));

            ImportedNote note = NoteAt(plan, "Note.md");
            Assert.Equal("2026-07-25T14:02:11.0000000Z", note.CreatedAt);
            Assert.Null(note.UpdatedAt);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Unreadable date"));
        }

        private static ImportPlan Build(List<Note> existing, params ImportFile[] files)
        {
            return MarkdownImportBuilder.Build(new ImportSource
            {
                Files = new List<ImportFile>(files),
                ExistingNotes = existing,
            });
        }

        private static ImportFile MakeFile(string path, string content)
        {
            return new ImportFile { Path = path, Content = content, SizeBytes = content.Length };
        }

        private static ImportFile MakeBinary(string path, long sizeBytes)
        {
            return new ImportFile { Path = path, SizeBytes = sizeBytes };
        }

        private static Note MakeExisting(string id, string title, string body)
        {
            return new Note
            {
                Id = id,
                Title = title,
                Body = body,
                Position = "a",
                CreatedAt = "2026-07-25T14:02:11.0000000Z",
                UpdatedAt = "2026-07-25T14:02:11.0000000Z",
            };
        }

        private static string Fenced(string id, string title, string body)
        {
            return "---\ntitle: " + title + "\ncreated: 2026-07-25 14:02:11Z\nupdated: 2026-07-25 14:02:11Z\nlorestead-id: " + id + "\n---\n\n" + body;
        }

        private static ImportedNote NoteAt(ImportPlan plan, string path)
        {
            ImportedNote result = null;
            foreach (ImportedNote note in plan.Notes)
            {
                if (note.Path == path)
                {
                    result = note;
                }
            }
            return result;
        }
    }
}
