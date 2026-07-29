using System;
using System.Collections.Generic;
using System.Text;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Export;
using SylvaNote.Core.Import;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class ImportApplyTests
    {
        [Fact]
        public void AppliesAVaultWithNotesLinksAndAnAttachment()
        {
            using TestDb db = new TestDb();
            byte[] image = { 1, 2, 3, 4 };

            ImportPlan plan = Build(db,
                MakeFile("Parent.md", "Down to [Child](Parent/Child.md)."),
                MakeFile("Parent/Child.md", "![pic](../attachments/pic.png)"),
                MakeBinary("attachments/pic.png", image.Length));
            ImportApplier.Apply(db.ConnectionManager, db.DeviceId, 50, plan, path => image);

            List<Note> notes = db.Notes.GetAll();
            Note parent = notes.Find(note => note.Title == "Parent");
            Note child = notes.Find(note => note.Title == "Child");
            Assert.Equal(2, notes.Count);
            Assert.Null(parent.ParentId);
            Assert.Equal(parent.Id, child.ParentId);
            Assert.Contains("note://" + child.Id, parent.Body);

            Attachment attachment = Assert.Single(db.Attachments.GetForNote(child.Id));
            Assert.Equal("pic.png", attachment.Filename);
            Assert.Equal(image, db.Attachments.GetBlob(attachment.Id));
            Assert.Contains("attachment://" + attachment.Id, child.Body);

            // The whole import rides the change log like hand-typed content.
            Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Note, parent.Id));
            Assert.Single(db.ChangeLog.GetForItem(ItemTypes.Attachment, attachment.Id));
            Assert.Single(db.Notes.GetBacklinks(child.Id));
        }

        [Fact]
        public void AMergeUpdatesInPlaceAndRecordsHistory()
        {
            using TestDb db = new TestDb();
            Note note = new Note
            {
                Id = Guid.CreateVersion7().ToString(),
                Title = "Note",
                Body = "Old body.",
                Position = "a",
            };
            db.Notes.Save(note);
            string createdAt = db.Notes.Get(note.Id).CreatedAt;

            ImportPlan plan = Build(db, MakeFile("Note.md",
                "---\ntitle: Note\nsylvanote-id: " + note.Id + "\n---\n\nNew body."));
            ImportApplier.Apply(db.ConnectionManager, db.DeviceId, 50, plan, path => null);

            Note merged = db.Notes.Get(note.Id);
            Assert.Equal("New body.", merged.Body);
            Assert.Equal(createdAt, merged.CreatedAt);
            Assert.Equal("a", merged.Position);
            Assert.Equal(2, db.ChangeLog.GetForItem(ItemTypes.Note, note.Id).Count);
        }

        [Fact]
        public void ReimportingAnExportChangesNothing()
        {
            using TestDb db = new TestDb();
            Note parent = new Note { Id = Guid.CreateVersion7().ToString(), Title = "Parent", Position = "a" };
            db.Notes.Save(parent);
            Note child = new Note
            {
                Id = Guid.CreateVersion7().ToString(),
                ParentId = parent.Id,
                Title = "Child",
                Position = "a",
            };
            db.Notes.Save(child);
            byte[] image = { 9, 8, 7 };
            Attachment attachment = new Attachment
            {
                Id = Guid.CreateVersion7().ToString(),
                NoteId = child.Id,
                Filename = "pic.png",
                MimeType = "image/png",
                SizeBytes = image.Length,
            };
            db.Attachments.Save(attachment);
            db.Attachments.SaveBlob(attachment.Id, image);
            child.Body = "Up to [Parent](note://" + parent.Id + ") and ![pic](attachment://" + attachment.Id + ")";
            db.Notes.Save(child);

            ExportLayout layout = MarkdownExportBuilder.Build(new ExportSource
            {
                Notes = db.Notes.GetAll(),
                Attachments = db.Attachments.GetAllForNotes(),
                GroupTemplates = true,
            });

            List<ImportFile> files = new List<ImportFile>();
            foreach (ExportedNote exported in layout.Notes)
            {
                files.Add(MakeFile(exported.Path, exported.Content));
            }
            foreach (ExportedAttachment exported in layout.Attachments)
            {
                files.Add(MakeBinary(exported.Path, image.Length));
            }

            int pendingBefore = db.ChangeLog.GetPending().Count;
            ImportPlan plan = Build(db, files.ToArray());
            ImportApplier.Apply(db.ConnectionManager, db.DeviceId, 50, plan, path => image);

            Assert.All(plan.Notes, note => Assert.Equal(ImportAction.SkipIdentical, note.Action));
            Assert.Empty(plan.Attachments);
            Assert.Empty(plan.Warnings);
            Assert.Equal(2, db.Notes.GetAll().Count);
            Assert.Single(db.Attachments.GetAllForNotes());
            Assert.Equal(pendingBefore, db.ChangeLog.GetPending().Count);
        }

        private static ImportPlan Build(TestDb db, params ImportFile[] files)
        {
            return MarkdownImportBuilder.Build(new ImportSource
            {
                Files = new List<ImportFile>(files),
                ExistingNotes = db.Notes.GetAll(),
                ExistingAttachments = db.Attachments.GetAllForNotes(),
            });
        }

        private static ImportFile MakeFile(string path, string content)
        {
            return new ImportFile
            {
                Path = path,
                Content = content,
                SizeBytes = Encoding.UTF8.GetByteCount(content),
            };
        }

        private static ImportFile MakeBinary(string path, long sizeBytes)
        {
            return new ImportFile { Path = path, SizeBytes = sizeBytes };
        }
    }
}
