using System;
using System.Collections.Generic;
using Lorestead.Core.Entities;
using Lorestead.Core.Import;
using Xunit;

namespace Lorestead.UnitTests
{
    // The RAW transform runs inside MarkdownImportBuilder.Build, so these tests
    // exercise the whole path: raw files in, canonical plan out.
    public sealed class JoplinRawImportTests
    {
        private const string NotebookId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaa0001";
        private const string NoteId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaa0002";
        private const string OtherNoteId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaa0003";
        private const string ResourceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaa0004";

        [Fact]
        public void ARawExportBecomesANotebookTreeWithStableIds()
        {
            ImportPlan plan = Build(
                RawFile(NotebookId, "Stories", "", "", 2),
                RawFile(NoteId, "Chapter One", "The body.", NotebookId));

            ImportedNote notebook = NoteAt(plan, "Stories.md");
            ImportedNote note = NoteAt(plan, "Stories/Chapter One.md");
            Assert.Equal("Stories", notebook.Title);
            Assert.Equal(ToGuid(NotebookId), notebook.Id);
            Assert.Equal("Chapter One", note.Title);
            Assert.Equal(ToGuid(NoteId), note.Id);
            Assert.Equal(notebook.Id, note.ParentId);
            Assert.Equal("The body.", note.Body);
            Assert.Equal("2023-06-25T17:31:11.7200000Z", note.CreatedAt);
            Assert.True(note.HadFrontMatterId);
        }

        [Fact]
        public void RawNoteLinksResolveToNoteLinks()
        {
            ImportPlan plan = Build(
                RawFile(NoteId, "One", "See [Two](:/" + OtherNoteId + ").", ""),
                RawFile(OtherNoteId, "Two", "Body.", ""));

            Assert.Contains("[Two](note://" + ToGuid(OtherNoteId) + ")", NoteAt(plan, "One.md").Body);
        }

        [Fact]
        public void RawResourceLinksBecomeAttachmentsWithTheirSidecarNames()
        {
            ImportPlan plan = Build(
                RawFile(NoteId, "Note", "![pic](:/" + ResourceId + ")", ""),
                RawFile(ResourceId, "my diagram.png", "", "", 4),
                new ImportFile { Path = "resources/" + ResourceId + ".png", SizeBytes = 5 });

            ImportedAttachment attachment = Assert.Single(plan.Attachments);
            Assert.Equal("my diagram.png", attachment.Filename);
            Assert.Equal("resources/" + ResourceId + ".png", attachment.SourcePath);
            Assert.Contains("![pic](attachment://" + attachment.Id + ")", NoteAt(plan, "Note.md").Body);
        }

        [Fact]
        public void TrashedEncryptedAndInternalItemsAreSkipped()
        {
            ImportFile trashedNote = RawFile(NoteId, "Gone", "Body.", "");
            trashedNote.Content = trashedNote.Content.Replace("deleted_time: 0", "deleted_time: 1690000000000");
            ImportFile encryptedNote = RawFile(OtherNoteId, "Secret", "cipher", "");
            encryptedNote.Content = encryptedNote.Content.Replace("encryption_applied: 0", "encryption_applied: 1");

            ImportPlan plan = Build(
                RawFile(NotebookId, "Keep", "", "", 2),
                trashedNote,
                encryptedNote,
                RawFile(ResourceId, "some tag", "", "", 5));

            Assert.Single(plan.Notes);
            Assert.Contains(plan.Warnings, warning => warning.Contains("Joplin's trash"));
            Assert.Contains(plan.Warnings, warning => warning.Contains("encrypted"));
            Assert.Contains(plan.Warnings, warning => warning.Contains("internal"));
        }

        [Fact]
        public void AnOrdinaryVaultIsNotMistakenForRaw()
        {
            ImportPlan plan = MarkdownImportBuilder.Build(new ImportSource
            {
                Files = new List<ImportFile>
                {
                    new ImportFile { Path = "Note.md", Content = "Plain body.", SizeBytes = 11 },
                },
            });

            ImportedNote note = Assert.Single(plan.Notes);
            Assert.Equal("Note", note.Title);
            Assert.Equal("Plain body.", note.Body);
        }

        [Fact]
        public void ReimportingARawImportMergesInsteadOfDuplicating()
        {
            ImportFile[] files =
            {
                RawFile(NotebookId, "Stories", "", "", 2),
                RawFile(NoteId, "Chapter One", "See [Two](:/" + OtherNoteId + ").", NotebookId),
                RawFile(OtherNoteId, "Two", "Body.", ""),
            };

            ImportPlan first = Build(files);

            List<Note> existing = new List<Note>();
            foreach (ImportedNote planned in first.Notes)
            {
                existing.Add(new Note
                {
                    Id = planned.Id,
                    Title = planned.Title,
                    Body = planned.Body,
                    Position = "a",
                });
            }

            ImportPlan second = MarkdownImportBuilder.Build(new ImportSource
            {
                Files = new List<ImportFile>(files),
                ExistingNotes = existing,
            });

            Assert.All(second.Notes, note => Assert.Equal(ImportAction.SkipIdentical, note.Action));
        }

        private static ImportPlan Build(params ImportFile[] files)
        {
            return MarkdownImportBuilder.Build(new ImportSource { Files = new List<ImportFile>(files) });
        }

        // The RAW shape: title line, blank, body, blank, metadata trailer. Content is
        // regenerated per call so tests can tweak individual metadata lines.
        private static ImportFile RawFile(string id, string title, string body, string parentId, int type = 1)
        {
            string content = title + "\n\n"
                + (body.Length > 0 ? body + "\n\n" : "")
                + "id: " + id + "\n"
                + "created_time: 2023-06-25T17:31:11.720Z\n"
                + "updated_time: 2023-06-25T17:31:11.720Z\n"
                + "user_created_time: 2023-06-25T17:31:11.720Z\n"
                + "user_updated_time: 2023-06-25T17:31:11.720Z\n"
                + "encryption_applied: 0\n"
                + "parent_id: " + parentId + "\n"
                + "deleted_time: 0\n"
                + "type_: " + type;
            return new ImportFile { Path = id + ".md", Content = content, SizeBytes = content.Length };
        }

        private static string ToGuid(string hexId)
        {
            return Guid.ParseExact(hexId, "N").ToString("D");
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
