using System.Collections.Generic;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Export;
using Xunit;

namespace SylvaNote.UnitTests
{
    public sealed class MarkdownExportTests
    {
        private const string RootId = "0198c0de-0000-7000-8000-000000000001";
        private const string ChildId = "0198c0de-0000-7000-8000-000000000002";
        private const string GrandchildId = "0198c0de-0000-7000-8000-000000000003";
        private const string OutsideId = "0198c0de-0000-7000-8000-0000000000ff";
        private const string AttachmentId = "0198c0de-1111-7000-8000-000000000001";

        [Fact]
        public void ANoteWithChildrenSitsBesideAFolderOfTheSameName()
        {
            ExportLayout layout = Build(
                MakeNote(RootId, null, "Parent", "a"),
                MakeNote(ChildId, RootId, "Child", "a"),
                MakeNote(GrandchildId, ChildId, "Grandchild", "a"));

            Assert.Equal("Parent.md", PathOf(layout, RootId));
            Assert.Equal("Parent/Child.md", PathOf(layout, ChildId));
            Assert.Equal("Parent/Child/Grandchild.md", PathOf(layout, GrandchildId));
        }

        [Fact]
        public void SiblingsAreOrderedByPositionAndDisambiguatedWhenTheySanitizeAlike()
        {
            ExportLayout layout = Build(
                MakeNote(ChildId, null, "Q3: Roadmap", "b"),
                MakeNote(RootId, null, "Q3/Roadmap", "a"));

            Assert.Equal("Q3 Roadmap.md", PathOf(layout, RootId));
            Assert.Equal("Q3 Roadmap (2).md", PathOf(layout, ChildId));
        }

        [Fact]
        public void FrontMatterCarriesTheTrueTitleInJoplinsDateSpelling()
        {
            Note note = MakeNote(RootId, null, "Q3: Roadmap", "a");
            note.CreatedAt = "2026-07-25T14:02:11.1234567Z";
            note.UpdatedAt = "2026-07-25T15:40:03.0000000Z";

            string content = Build(note).Notes[0].Content;

            Assert.StartsWith("---\n", content);
            Assert.Contains("title: \"Q3: Roadmap\"\n", content);
            Assert.Contains("created: 2026-07-25 14:02:11Z\n", content);
            Assert.Contains("updated: 2026-07-25 15:40:03Z\n", content);
            Assert.Contains("sylvanote-id: " + RootId + "\n", content);
        }

        [Fact]
        public void PlainTitlesAreNotQuoted()
        {
            Assert.Contains("title: Meeting notes\n", Build(MakeNote(RootId, null, "Meeting notes", "a")).Notes[0].Content);
        }

        [Fact]
        public void AnInScopeNoteLinkBecomesARelativePath()
        {
            Note root = MakeNote(RootId, null, "Parent", "a");
            Note child = MakeNote(ChildId, RootId, "Child", "a");
            child.Body = "Up to [Parent](note://" + RootId + ").";

            ExportLayout layout = Build(root, child);

            Assert.Contains("[Parent](../Parent.md)", ContentOf(layout, ChildId));
        }

        [Fact]
        public void AnOutOfScopeNoteLinkIsLeftUntouched()
        {
            Note root = MakeNote(RootId, null, "Parent", "a");
            root.Body = "Elsewhere: [Gone](note://" + OutsideId + ").";

            Assert.Contains("[Gone](note://" + OutsideId + ")", Build(root).Notes[0].Content);
        }

        [Fact]
        public void AttachmentsLandInOneRootFolderWithRelativeLinks()
        {
            Note root = MakeNote(RootId, null, "Parent", "a");
            Note child = MakeNote(ChildId, RootId, "Child", "a");
            child.Body = "![diagram](attachment://" + AttachmentId + ")";

            ExportLayout layout = MarkdownExportBuilder.Build(new ExportSource
            {
                Notes = new List<Note> { root, child },
                Attachments = new List<Attachment>
                {
                    new Attachment { Id = AttachmentId, NoteId = ChildId, Filename = "my diagram.png" },
                },
            });

            Assert.Equal("attachments/my diagram.png", layout.Attachments[0].Path);
            Assert.Contains("![diagram](../attachments/my%20diagram.png)", ContentOf(layout, ChildId));
        }

        [Fact]
        public void TrashedNotesAreExcludedAndTheirChildrenBecomeRoots()
        {
            Note root = MakeNote(RootId, null, "Parent", "a");
            root.Deleted = true;

            ExportLayout layout = Build(root, MakeNote(ChildId, RootId, "Child", "a"));

            Assert.Single(layout.Notes);
            Assert.Equal("Child.md", PathOf(layout, ChildId));
        }

        [Fact]
        public void TemplateRootsGroupUnderTemplatesOnlyForTheWholeTree()
        {
            Note template = MakeNote(RootId, null, "Project", "a");
            template.Type = NoteType.Template;
            Note child = MakeNote(ChildId, RootId, "Section", "a");

            ExportLayout grouped = MarkdownExportBuilder.Build(new ExportSource
            {
                Notes = new List<Note> { template, child },
                GroupTemplates = true,
            });
            Assert.Equal("Templates/Project.md", PathOf(grouped, RootId));
            Assert.Equal("Templates/Project/Section.md", PathOf(grouped, ChildId));

            Assert.Equal("Project.md", PathOf(Build(template, child), RootId));
        }

        [Fact]
        public void NamesIllegalOnWindowsAreRepaired()
        {
            Assert.Equal("Untitled", ExportFileName.Sanitize(""));
            Assert.Equal("What's next", ExportFileName.Sanitize("What's next?"));
            Assert.Equal("Auth Session flow", ExportFileName.Sanitize("Auth / Session flow"));
            Assert.Equal("CON_", ExportFileName.Sanitize("CON"));
            Assert.Equal("Trailing", ExportFileName.Sanitize("Trailing... "));
            Assert.Equal("notes.txt", ExportFileName.SanitizeAttachment("notes.txt"));
            Assert.Equal("a b.png", ExportFileName.SanitizeAttachment("a\tb.png"));
        }

        private static ExportLayout Build(params Note[] notes)
        {
            return MarkdownExportBuilder.Build(new ExportSource { Notes = new List<Note>(notes) });
        }

        private static Note MakeNote(string id, string parentId, string title, string position)
        {
            return new Note
            {
                Id = id,
                ParentId = parentId,
                Title = title,
                Body = string.Empty,
                Position = position,
                CreatedAt = "2026-07-25T14:02:11.0000000Z",
                UpdatedAt = "2026-07-25T14:02:11.0000000Z",
            };
        }

        private static string PathOf(ExportLayout layout, string id)
        {
            string path = null;
            foreach (ExportedNote note in layout.Notes)
            {
                if (note.Id == id)
                {
                    path = note.Path;
                }
            }
            return path;
        }

        private static string ContentOf(ExportLayout layout, string id)
        {
            string content = null;
            foreach (ExportedNote note in layout.Notes)
            {
                if (note.Id == id)
                {
                    content = note.Content;
                }
            }
            return content;
        }
    }
}
