using System;
using System.Collections.Generic;
using System.Linq;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    public sealed class VaultRepositoryTests
    {
        private static readonly byte[] Bytes1 = { 1, 1, 1, 1 };
        private static readonly byte[] Bytes2 = { 2, 2, 2, 2 };
        private static readonly byte[] Bytes3 = { 3, 3, 3, 3 };

        [Fact]
        public void CreateWritesTheVaultAndBothKeysAsPendingChanges()
        {
            using TestDb db = new TestDb();
            Vault vault = Items.Vault();
            VaultKey password = Items.VaultKey(VaultKeyKind.Password, Bytes1, salt: Bytes2);
            VaultKey recovery = Items.VaultKey(VaultKeyKind.Recovery, Bytes3);

            db.Vault.Create(vault, password, recovery);

            Assert.Equal(vault.Id, db.Vault.Get().Id);
            List<VaultKey> keys = db.Vault.GetKeys(vault.Id);
            Assert.Equal(2, keys.Count);
            Assert.Equal(Bytes2, db.Vault.GetKey(vault.Id, VaultKeyKind.Password).KdfSalt);
            Assert.Equal(256, db.Vault.GetKey(vault.Id, VaultKeyKind.Password).KdfMemoryKiB);
            Assert.Null(db.Vault.GetKey(vault.Id, VaultKeyKind.Recovery).KdfSalt);

            List<PendingChange> pending = db.ChangeLog.GetPending();
            Assert.Equal(3, pending.Count);
            Assert.Single(pending, p => p.Entry.ItemType == ItemTypes.Vault && p.Entry.ItemId == vault.Id);
            Assert.Equal(2, pending.Count(p => p.Entry.ItemType == ItemTypes.VaultKey));
            Assert.All(pending, p => Assert.Null(p.Entry.BaseSeq));
        }

        [Fact]
        public void CreateRefusesASecondVault()
        {
            using TestDb db = new TestDb();
            db.Vault.Create(Items.Vault(), Items.VaultKey(VaultKeyKind.Password, Bytes1, Bytes2), Items.VaultKey(VaultKeyKind.Recovery, Bytes3));

            Assert.Throws<InvalidOperationException>(() =>
                db.Vault.Create(Items.Vault(), Items.VaultKey(VaultKeyKind.Password, Bytes1, Bytes2), Items.VaultKey(VaultKeyKind.Recovery, Bytes3)));
            Assert.Single(db.ChangeLog.GetPending(), p => p.Entry.ItemType == ItemTypes.Vault);
        }

        [Fact]
        public void SaveKeyRewrapsInPlaceAndKeepsHistory()
        {
            using TestDb db = new TestDb();
            Vault vault = Items.Vault();
            VaultKey password = Items.VaultKey(VaultKeyKind.Password, Bytes1, Bytes2);
            db.Vault.Create(vault, password, Items.VaultKey(VaultKeyKind.Recovery, Bytes3));

            password.WrappedKey = Bytes3;
            password.KdfSalt = Bytes1;
            db.Vault.SaveKey(password);

            VaultKey stored = db.Vault.GetKey(vault.Id, VaultKeyKind.Password);
            Assert.Equal(password.Id, stored.Id);
            Assert.Equal(Bytes3, stored.WrappedKey);
            Assert.Equal(Bytes1, stored.KdfSalt);
            Assert.Equal(2, db.ChangeLog.GetForItem(ItemTypes.VaultKey, password.Id).Count);
            Assert.Equal(2, db.Vault.GetKeys(vault.Id).Count);
        }

        [Fact]
        public void ItemsRoundTripCiphertextAndTheIndexReadSkipsBodies()
        {
            using TestDb db = new TestDb();
            Vault vault = CreateVault(db);
            VaultItem item = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);

            db.VaultItems.Save(item);

            VaultItem stored = db.VaultItems.Get(item.Id);
            Assert.Equal(Bytes1, stored.TitleEnc);
            Assert.Equal(Bytes2, stored.BodyEnc);
            Assert.Equal(Bytes3, stored.LinksEnc);
            Assert.False(string.IsNullOrEmpty(stored.CreatedAt));

            VaultItem indexed = Assert.Single(db.VaultItems.GetAllWithoutBodies());
            Assert.Equal(Bytes1, indexed.TitleEnc);
            Assert.Null(indexed.BodyEnc);

            ChangeLogEntry entry = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.VaultItem, item.Id));
            Assert.Equal(ChangeOps.Upsert, entry.Op);
            Assert.Equal(item.Id, PayloadJson.Deserialize<VaultItem>(entry.Payload).Id);
            Assert.Equal(Bytes2, PayloadJson.Deserialize<VaultItem>(entry.Payload).BodyEnc);
        }

        [Fact]
        public void TrashAndRestoreActOnTheSubtree()
        {
            using TestDb db = new TestDb();
            Vault vault = CreateVault(db);
            VaultItem parent = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);
            VaultItem child = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3, parentId: parent.Id);
            db.VaultItems.Save(parent);
            db.VaultItems.Save(child);

            db.VaultItems.TrashSubtree(parent.Id);
            Assert.True(db.VaultItems.Get(parent.Id).Deleted);
            Assert.True(db.VaultItems.Get(child.Id).Deleted);

            db.VaultItems.RestoreSubtree(parent.Id);
            Assert.False(db.VaultItems.Get(parent.Id).Deleted);
            Assert.False(db.VaultItems.Get(child.Id).Deleted);
            Assert.Equal(parent.Id, db.VaultItems.Get(child.Id).ParentId);
        }

        [Fact]
        public void RestoringAChildUnderATrashedParentMovesItToRoot()
        {
            using TestDb db = new TestDb();
            Vault vault = CreateVault(db);
            VaultItem parent = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);
            VaultItem child = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3, parentId: parent.Id);
            db.VaultItems.Save(parent);
            db.VaultItems.Save(child);
            db.VaultItems.TrashSubtree(parent.Id);

            db.VaultItems.RestoreSubtree(child.Id);

            Assert.Null(db.VaultItems.Get(child.Id).ParentId);
            Assert.True(db.VaultItems.Get(parent.Id).Deleted);
        }

        [Fact]
        public void PurgeCascadesToAttachmentsAndDeletesHistoryOnBothTypes()
        {
            using TestDb db = new TestDb();
            Vault vault = CreateVault(db);
            VaultItem item = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);
            db.VaultItems.Save(item);
            item.TitleEnc = Bytes2;
            db.VaultItems.Save(item);
            VaultAttachment attachment = Items.VaultAttachment(item.Id, Bytes1, Bytes2);
            db.VaultAttachments.Save(attachment);
            db.VaultAttachments.SaveBlob(attachment.Id, Bytes3);

            db.VaultItems.PurgeSubtree(item.Id);

            Assert.Null(db.VaultItems.Get(item.Id));
            Assert.Null(db.VaultAttachments.Get(attachment.Id));
            Assert.Null(db.VaultAttachments.GetBlob(attachment.Id));
            ChangeLogEntry itemEntry = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.VaultItem, item.Id));
            Assert.Equal(ChangeOps.Purge, itemEntry.Op);
            ChangeLogEntry attachmentEntry = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.VaultAttachment, attachment.Id));
            Assert.Equal(ChangeOps.Purge, attachmentEntry.Op);
        }

        [Fact]
        public void ExpiredTrashPurgesOnlyTombstonesOlderThanTheCutoff()
        {
            using TestDb db = new TestDb();
            Vault vault = CreateVault(db);
            VaultItem trashed = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);
            VaultItem live = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);
            db.VaultItems.Save(trashed);
            db.VaultItems.Save(live);
            db.VaultItems.TrashSubtree(trashed.Id);

            db.VaultItems.PurgeExpiredTrash("9999-01-01T00:00:00.0000000Z");

            Assert.Null(db.VaultItems.Get(trashed.Id));
            Assert.NotNull(db.VaultItems.Get(live.Id));
        }

        [Fact]
        public void MissingBlobQueryIgnoresSoftDeletedAttachments()
        {
            using TestDb db = new TestDb();
            Vault vault = CreateVault(db);
            VaultItem item = Items.VaultItem(vault.Id, Bytes1, Bytes2, Bytes3);
            db.VaultItems.Save(item);
            VaultAttachment waiting = Items.VaultAttachment(item.Id, Bytes1, Bytes2);
            VaultAttachment removed = Items.VaultAttachment(item.Id, Bytes1, Bytes2);
            db.VaultAttachments.Save(waiting);
            db.VaultAttachments.Save(removed);
            removed.Deleted = true;
            db.VaultAttachments.Save(removed);

            Assert.Equal(new[] { waiting.Id }, db.VaultAttachments.GetIdsMissingBlob());
            Assert.Single(db.VaultAttachments.GetForItem(item.Id));

            db.VaultAttachments.SaveBlob(waiting.Id, Bytes3);
            Assert.Empty(db.VaultAttachments.GetIdsMissingBlob());
            Assert.Equal(Bytes3, db.VaultAttachments.GetBlob(waiting.Id));
        }

        private static Vault CreateVault(TestDb db)
        {
            Vault vault = Items.Vault();
            db.Vault.Create(vault, Items.VaultKey(VaultKeyKind.Password, Bytes1, Bytes2), Items.VaultKey(VaultKeyKind.Recovery, Bytes3));
            return vault;
        }
    }
}
