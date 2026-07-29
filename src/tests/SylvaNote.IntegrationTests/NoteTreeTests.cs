using System.Collections.Generic;
using System.Linq;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Search;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.IntegrationTests
{
    public sealed class NoteTreeTests
    {
        [Fact]
        public void TrashSubtree_TombstonesEveryDescendant_WithOutboxEntryEach()
        {
            using TestDb db = new TestDb();
            Note root = Items.Note("Root");
            Note child = Items.Note("Child", parentId: root.Id);
            Note grandchild = Items.Note("Grandchild", parentId: child.Id);
            db.Notes.Save(root);
            db.Notes.Save(child);
            db.Notes.Save(grandchild);

            db.Notes.TrashSubtree(root.Id);

            Assert.True(db.Notes.Get(root.Id).Deleted);
            Assert.True(db.Notes.Get(child.Id).Deleted);
            Assert.True(db.Notes.Get(grandchild.Id).Deleted);
            Assert.Equal(root.Id, db.Notes.Get(child.Id).ParentId);
            Assert.Equal(6, db.ChangeLog.GetPending().Count);
        }

        [Fact]
        public void RestoreSubtree_InPlace_WhenParentAlive()
        {
            using TestDb db = new TestDb();
            Note parent = Items.Note("Parent");
            Note child = Items.Note("Child", parentId: parent.Id);
            db.Notes.Save(parent);
            db.Notes.Save(child);

            db.Notes.TrashSubtree(child.Id);
            db.Notes.RestoreSubtree(child.Id);

            Note restored = db.Notes.Get(child.Id);
            Assert.False(restored.Deleted);
            Assert.Equal(parent.Id, restored.ParentId);
        }

        [Fact]
        public void RestoreSubtree_OrphanChild_GoesToRootLevel()
        {
            using TestDb db = new TestDb();
            Note parent = Items.Note("Parent");
            Note child = Items.Note("Child", parentId: parent.Id);
            Note grandchild = Items.Note("Grandchild", parentId: child.Id);
            db.Notes.Save(parent);
            db.Notes.Save(child);
            db.Notes.Save(grandchild);

            db.Notes.TrashSubtree(parent.Id);
            db.Notes.RestoreSubtree(child.Id);

            Note restoredChild = db.Notes.Get(child.Id);
            Assert.False(restoredChild.Deleted);
            Assert.Null(restoredChild.ParentId);
            Assert.False(db.Notes.Get(grandchild.Id).Deleted);
            Assert.True(db.Notes.Get(parent.Id).Deleted);
        }

        [Fact]
        public void RestoreSubtreeAt_PlacesRootAtDropLocation()
        {
            using TestDb db = new TestDb();
            Note target = Items.Note("Target");
            Note note = Items.Note("Dragged");
            db.Notes.Save(target);
            db.Notes.Save(note);

            db.Notes.TrashSubtree(note.Id);
            db.Notes.RestoreSubtreeAt(note.Id, target.Id, "b");

            Note restored = db.Notes.Get(note.Id);
            Assert.False(restored.Deleted);
            Assert.Equal(target.Id, restored.ParentId);
            Assert.Equal("b", restored.Position);
        }

        [Fact]
        public void RestoreWithAncestors_RestoresTopmostTrashedSubtree()
        {
            using TestDb db = new TestDb();
            Note parent = Items.Note("Parent");
            Note child = Items.Note("Child", parentId: parent.Id);
            Note grandchild = Items.Note("Grandchild", parentId: child.Id);
            db.Notes.Save(parent);
            db.Notes.Save(child);
            db.Notes.Save(grandchild);

            db.Notes.TrashSubtree(parent.Id);
            db.Notes.RestoreWithAncestors(grandchild.Id);

            Assert.False(db.Notes.Get(parent.Id).Deleted);
            Assert.False(db.Notes.Get(child.Id).Deleted);
            Assert.False(db.Notes.Get(grandchild.Id).Deleted);
            Assert.Equal(parent.Id, db.Notes.Get(child.Id).ParentId);
        }

        [Fact]
        public void PurgeSubtree_RemovesRowsBlobsAndHistory_AppendsPendingPurges()
        {
            using TestDb db = new TestDb();
            Note root = Items.Note("Root");
            Note child = Items.Note("Child", parentId: root.Id);
            db.Notes.Save(root);
            db.Notes.Save(child);
            Attachment attachment = Items.Attachment(noteId: child.Id);
            db.Attachments.Save(attachment);
            db.Attachments.SaveBlob(attachment.Id, new byte[] { 1, 2, 3 });

            db.Notes.PurgeSubtree(root.Id);

            Assert.Null(db.Notes.Get(root.Id));
            Assert.Null(db.Notes.Get(child.Id));
            Assert.Null(db.Attachments.Get(attachment.Id));
            Assert.Null(db.Attachments.GetBlob(attachment.Id));

            List<PendingChange> pending = db.ChangeLog.GetPending();
            List<PendingChange> purges = pending.Where(p => p.Entry.Op == ChangeOps.Purge).ToList();
            Assert.Equal(3, purges.Count);
            Assert.Equal(3, pending.Count);
            Assert.Contains(purges, p => p.Entry.ItemType == ItemTypes.Attachment && p.Entry.ItemId == attachment.Id);
            Assert.Contains(purges, p => p.Entry.ItemType == ItemTypes.Note && p.Entry.ItemId == root.Id);
            Assert.Contains(purges, p => p.Entry.ItemType == ItemTypes.Note && p.Entry.ItemId == child.Id);
        }

        [Fact]
        public void PurgeExpiredTrash_HonorsCutoff()
        {
            using TestDb db = new TestDb();
            Note expired = Items.Note("Expired");
            Note fresh = Items.Note("Fresh");
            Note alive = Items.Note("Alive");
            db.Notes.Save(expired);
            db.Notes.Save(fresh);
            db.Notes.Save(alive);
            db.Notes.TrashSubtree(expired.Id);
            db.Notes.TrashSubtree(fresh.Id);

            db.Notes.PurgeExpiredTrash("0000-01-01T00:00:00.000Z");
            Assert.NotNull(db.Notes.Get(expired.Id));

            db.Notes.PurgeExpiredTrash("9999-01-01T00:00:00.000Z");
            Assert.Null(db.Notes.Get(expired.Id));
            Assert.Null(db.Notes.Get(fresh.Id));
            Assert.NotNull(db.Notes.Get(alive.Id));
        }

        [Fact]
        public void InstantiateTemplate_DeepCopiesWithNewIds()
        {
            using TestDb db = new TestDb();
            Note templateRoot = Items.Note("My Template", body: "root body");
            templateRoot.Type = NoteType.Template;
            Note templateChild = Items.Note("Step 1", body: "child body", parentId: templateRoot.Id);
            Note target = Items.Note("Projects");
            db.Notes.Save(templateRoot);
            db.Notes.Save(templateChild);
            db.Notes.Save(target);

            string newRootId = db.Notes.InstantiateTemplate(templateRoot.Id, "New Project", target.Id, "m");

            Note newRoot = db.Notes.Get(newRootId);
            Assert.NotEqual(templateRoot.Id, newRootId);
            Assert.Equal("New Project", newRoot.Title);
            Assert.Equal("root body", newRoot.Body);
            Assert.Equal(NoteType.Normal, newRoot.Type);
            Assert.Equal(target.Id, newRoot.ParentId);
            Assert.Equal("m", newRoot.Position);

            List<Note> all = db.Notes.GetAll();
            Note newChild = all.Single(n => n.ParentId == newRootId);
            Assert.NotEqual(templateChild.Id, newChild.Id);
            Assert.Equal("Step 1", newChild.Title);
            Assert.Equal(NoteType.Normal, newChild.Type);

            Assert.Equal("My Template", db.Notes.Get(templateRoot.Id).Title);
        }

        [Fact]
        public void InstantiateTemplate_CopiesAttachmentsWithBlobAndThumbnail()
        {
            using TestDb db = new TestDb();
            Note templateRoot = Items.Note("Letterhead template");
            templateRoot.Type = NoteType.Template;
            Note templateChild = Items.Note("Section", parentId: templateRoot.Id);
            templateChild.Type = NoteType.Template;
            db.Notes.Save(templateRoot);
            db.Notes.Save(templateChild);

            Attachment logo = Items.Attachment(noteId: templateRoot.Id, filename: "logo.png");
            db.Attachments.Save(logo);
            db.Attachments.SaveBlob(logo.Id, new byte[] { 9, 8, 7 });
            db.Attachments.SaveThumbnail(logo.Id, new byte[] { 1, 2 });

            Attachment childFile = Items.Attachment(noteId: templateChild.Id, filename: "notes.txt");
            db.Attachments.Save(childFile);
            db.Attachments.SaveBlob(childFile.Id, new byte[] { 4 });

            string newRootId = db.Notes.InstantiateTemplate(templateRoot.Id, "New Project", null, "m");

            Attachment copied = Assert.Single(db.Attachments.GetForNote(newRootId));
            Assert.NotEqual(logo.Id, copied.Id);
            Assert.Equal("logo.png", copied.Filename);
            Assert.Equal(3, copied.SizeBytes);
            Assert.Equal(new byte[] { 9, 8, 7 }, db.Attachments.GetBlob(copied.Id));
            Assert.Equal(new byte[] { 1, 2 }, db.Attachments.GetThumbnail(copied.Id));

            // Descendants of the template carry their attachments too.
            Note newChild = db.Notes.GetAll().Single(n => n.ParentId == newRootId);
            Attachment copiedChildFile = Assert.Single(db.Attachments.GetForNote(newChild.Id));
            Assert.Equal("notes.txt", copiedChildFile.Filename);
            Assert.Equal(new byte[] { 4 }, db.Attachments.GetBlob(copiedChildFile.Id));

            // The template keeps its own attachment, untouched.
            Assert.Equal(logo.Id, Assert.Single(db.Attachments.GetForNote(templateRoot.Id)).Id);
        }

        [Fact]
        public void DuplicateSubtree_DeepCopiesAsSibling_PreservingType()
        {
            using TestDb db = new TestDb();
            Note root = Items.Note("Recipe", body: "root body");
            root.Type = NoteType.Template;
            Note child = Items.Note("Step", body: "child body", parentId: root.Id);
            db.Notes.Save(root);
            db.Notes.Save(child);

            string newRootId = db.Notes.DuplicateSubtree(root.Id, "Recipe Copy", "m");

            Note copy = db.Notes.Get(newRootId);
            Assert.NotEqual(root.Id, newRootId);
            Assert.Equal("Recipe Copy", copy.Title);
            Assert.Equal("root body", copy.Body);
            Assert.Equal(NoteType.Template, copy.Type);
            Assert.Equal(root.ParentId, copy.ParentId);
            Assert.Equal("m", copy.Position);

            Note copiedChild = db.Notes.GetAll().Single(n => n.ParentId == newRootId);
            Assert.NotEqual(child.Id, copiedChild.Id);
            Assert.Equal("Step", copiedChild.Title);
            Assert.Equal("child body", copiedChild.Body);

            Assert.Equal("Recipe", db.Notes.Get(root.Id).Title);
        }

        [Fact]
        public void DuplicateSubtree_LeavesTrashedDescendantsBehind()
        {
            using TestDb db = new TestDb();
            Note root = Items.Note("Root");
            Note kept = Items.Note("Kept", parentId: root.Id);
            Note trashed = Items.Note("Trashed", parentId: root.Id);
            db.Notes.Save(root);
            db.Notes.Save(kept);
            db.Notes.Save(trashed);
            db.Notes.TrashSubtree(trashed.Id);

            string newRootId = db.Notes.DuplicateSubtree(root.Id, "Root Copy", "m");

            Note copiedChild = db.Notes.GetAll().Single(n => n.ParentId == newRootId);
            Assert.Equal("Kept", copiedChild.Title);
        }

        [Fact]
        public void GetNextChildPosition_ReturnsNearestKeyAbove()
        {
            using TestDb db = new TestDb();
            Note first = Items.Note("First");
            first.Position = "b";
            Note second = Items.Note("Second");
            second.Position = "m";
            Note last = Items.Note("Last");
            last.Position = "x";
            db.Notes.Save(first);
            db.Notes.Save(second);
            db.Notes.Save(last);

            Assert.Equal("m", db.Notes.GetNextChildPosition(null, "b"));
            Assert.Null(db.Notes.GetNextChildPosition(null, "x"));
        }

        [Fact]
        public void SearchNotes_IncludeTrashed_FindsTombstonedNotes()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Zeppelin maintenance", body: "hydrogen checklist");
            db.Notes.Save(note);
            db.Notes.TrashSubtree(note.Id);

            Assert.Empty(db.Search.SearchNotes("zeppelin"));
            List<SearchResult> found = db.Search.SearchNotes("zeppelin", includeTrashed: true);
            Assert.Single(found);
            Assert.Equal(note.Id, found[0].Id);
        }

        [Fact]
        public void SearchNotes_QuerySyntaxCharacters_DoNotThrow()
        {
            using TestDb db = new TestDb();
            db.Notes.Save(Items.Note("Parens", body: "function(arg) body"));

            Assert.Single(db.Search.SearchNotes("function(arg"));
            Assert.Empty(db.Search.SearchNotes("\"unbalanced"));
            Assert.Empty(db.Search.SearchNotes("   "));
        }

        [Fact]
        public void SearchNotes_PrefixMatches_ForTypeAhead()
        {
            using TestDb db = new TestDb();
            db.Notes.Save(Items.Note("Kubernetes cluster notes"));

            Assert.Single(db.Search.SearchNotes("kuber"));
        }
    }
}
