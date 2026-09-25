using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Lorestead.Core.Crypto;
using Lorestead.Core.DataAccess.Migrations;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.IntegrationTests
{
    public sealed class VaultSyncTests
    {
        private const string OtherDevice = "0198c0de-aaaa-7000-8000-00000000dev2";
        private const string Password = "correct horse battery staple";

        private static readonly KdfParameters TestKdf = new KdfParameters { MemoryKiB = 256, Iterations = 2, Parallelism = 2 };
        private static readonly byte[] Bytes1 = { 1, 1, 1, 1 };

        [Fact]
        public void ApplierCommitsAChildThatArrivesBeforeItsParent()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);

            Vault vault = Stamp(Items.Vault());
            VaultItem parent = Stamp(Items.VaultItem(vault.Id, Bytes1, Bytes1, Bytes1));
            VaultItem child = Stamp(Items.VaultItem(vault.Id, Bytes1, Bytes1, Bytes1, parentId: parent.Id));

            applier.Apply(new List<ChangeLogEntry>
            {
                Foreign(ItemTypes.Vault, vault.Id, vault, 1),
                Foreign(ItemTypes.VaultItem, child.Id, child, 2),
                Foreign(ItemTypes.VaultItem, parent.Id, parent, 3),
            });

            Assert.NotNull(db.VaultItems.Get(child.Id));
            Assert.Equal(parent.Id, db.VaultItems.Get(child.Id).ParentId);
            Assert.Equal(3, db.SyncState.Get().LastSeenSeq);
        }

        [Fact]
        public void ApplierPurgeRemovesTheRowAndItsHistory()
        {
            using TestDb db = new TestDb();
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            ChangeApplier applier = new ChangeApplier(db.ConnectionManager, db.DeviceId);
            Vault vault = Stamp(Items.Vault());
            VaultItem item = Stamp(Items.VaultItem(vault.Id, Bytes1, Bytes1, Bytes1));
            VaultAttachment attachment = Stamp(Items.VaultAttachment(item.Id, Bytes1, Bytes1));

            applier.Apply(new List<ChangeLogEntry>
            {
                Foreign(ItemTypes.Vault, vault.Id, vault, 1),
                Foreign(ItemTypes.VaultItem, item.Id, item, 2),
                Foreign(ItemTypes.VaultAttachment, attachment.Id, attachment, 3),
            });
            db.VaultAttachments.SaveBlob(attachment.Id, Bytes1);

            applier.Apply(new List<ChangeLogEntry>
            {
                Purge(ItemTypes.VaultAttachment, attachment.Id, 4),
                Purge(ItemTypes.VaultItem, item.Id, 5),
            });

            Assert.Null(db.VaultItems.Get(item.Id));
            Assert.Null(db.VaultAttachments.Get(attachment.Id));
            Assert.Null(db.VaultAttachments.GetBlob(attachment.Id));
            ChangeLogEntry remaining = Assert.Single(db.ChangeLog.GetForItem(ItemTypes.VaultItem, item.Id));
            Assert.Equal(ChangeOps.Purge, remaining.Op);
        }

        [Fact]
        public void IngestMaterializesVaultRowsAndTouchesNoPlaintextTables()
        {
            using TestDb db = new TestDb(MigrationSets.Server());
            ChangeIngestor ingestor = new ChangeIngestor(db.ConnectionManager);
            Vault vault = Stamp(Items.Vault());
            VaultKey key = Stamp(Items.VaultKey(VaultKeyKind.Password, Bytes1, Bytes1));
            key.VaultId = vault.Id;
            VaultItem item = Stamp(Items.VaultItem(vault.Id, Encoding.UTF8.GetBytes("needle"), Bytes1, Bytes1));
            VaultAttachment attachment = Stamp(Items.VaultAttachment(item.Id, Bytes1, Bytes1));

            UploadChangesResponse response = ingestor.Ingest(new List<ChangeLogEntry>
            {
                Foreign(ItemTypes.Vault, vault.Id, vault, null),
                Foreign(ItemTypes.VaultKey, key.Id, key, null),
                Foreign(ItemTypes.VaultItem, item.Id, item, null),
                Foreign(ItemTypes.VaultAttachment, attachment.Id, attachment, null),
            });

            Assert.Equal(4, response.Results.Count);
            Assert.All(response.Results, r => Assert.False(r.SupersededConcurrent));
            Assert.Equal(vault.Id, db.Vault.Get().Id);
            Assert.Equal(Bytes1, db.Vault.GetKey(vault.Id, VaultKeyKind.Password).WrappedKey);
            Assert.Equal("needle", Encoding.UTF8.GetString(db.VaultItems.Get(item.Id).TitleEnc));
            Assert.NotNull(db.VaultAttachments.Get(attachment.Id));
            Assert.Empty(db.Notes.GetAll());
            Assert.Empty(db.Search.SearchNotes("needle"));
        }

        [Fact]
        public async Task VaultRoundTripsThroughTheServerAndUnlocksOnASecondDevice()
        {
            using ServerFixture server = new ServerFixture();
            using TestDb deviceA = new TestDb();
            using TestDb deviceB = new TestDb();
            using HttpClient httpA = new HttpClient();
            using HttpClient httpB = new HttpClient();
            SyncCycle cycleA = CycleFor(deviceA, server, httpA);
            SyncCycle cycleB = CycleFor(deviceB, server, httpB);
            IPasswordKdf kdf = new Argon2idKdf();

            // Device A creates the vault and writes one note with one attachment.
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] recoveryKey = VaultKeys.GenerateRecoveryKey();
            Vault vault = Items.Vault();
            VaultKey passwordRow = Items.VaultKey(VaultKeyKind.Password, null, VaultKeys.GenerateSalt());
            VaultKey recoveryRow = Items.VaultKey(VaultKeyKind.Recovery, null);
            byte[] passwordKey = VaultKeys.DerivePasswordKey(kdf, Password, passwordRow.KdfSalt, TestKdf);
            passwordRow.WrappedKey = VaultKeys.Wrap(passwordKey, vaultKey, VaultBinding.ForKeyRow(vault.Id, passwordRow.Id, (int)VaultKeyKind.Password));
            recoveryRow.WrappedKey = VaultKeys.Wrap(VaultKeys.RecoveryWrappingKey(recoveryKey), vaultKey, VaultBinding.ForKeyRow(vault.Id, recoveryRow.Id, (int)VaultKeyKind.Recovery));
            deviceA.Vault.Create(vault, passwordRow, recoveryRow);

            VaultItem item = Items.VaultItem(vault.Id, null, null, null);
            item.TitleEnc = VaultCipher.Encrypt(vaultKey, Encoding.UTF8.GetBytes("Secret title"), VaultBinding.ForField(item.Id, "title", 1));
            item.BodyEnc = VaultCipher.Encrypt(vaultKey, Encoding.UTF8.GetBytes("Secret body"), VaultBinding.ForField(item.Id, "body", 1));
            item.LinksEnc = VaultCipher.Encrypt(vaultKey, Encoding.UTF8.GetBytes(""), VaultBinding.ForField(item.Id, "links", 1));
            deviceA.VaultItems.Save(item);

            VaultAttachment attachment = Items.VaultAttachment(item.Id, null, null);
            attachment.NameEnc = VaultCipher.Encrypt(vaultKey, Encoding.UTF8.GetBytes("photo.png"), VaultBinding.ForField(attachment.Id, "name", 1));
            attachment.MimeEnc = VaultCipher.Encrypt(vaultKey, Encoding.UTF8.GetBytes("image/png"), VaultBinding.ForField(attachment.Id, "mime", 1));
            deviceA.VaultAttachments.Save(attachment);
            deviceA.VaultAttachments.SaveBlob(attachment.Id, VaultCipher.Encrypt(vaultKey, new byte[] { 9, 8, 7 }, VaultBinding.ForField(attachment.Id, "blob", 1)));

            SyncCycleResult uploaded = await cycleA.Run();
            SyncCycleResult downloaded = await cycleB.Run();

            Assert.Equal(5, uploaded.Uploaded);
            Assert.Equal(1, uploaded.BlobsUploaded);
            Assert.Equal(1, downloaded.BlobsDownloaded);
            Assert.Equal(SyncProtocol.Version, downloaded.ServerProtocolVersion);
            Assert.Contains(ItemTypes.VaultItem, downloaded.ChangedItemTypes);

            // Device B unlocks with the password using the parameters stored on the row.
            VaultKey syncedPasswordRow = deviceB.Vault.GetKey(vault.Id, VaultKeyKind.Password);
            KdfParameters syncedKdf = new KdfParameters
            {
                MemoryKiB = syncedPasswordRow.KdfMemoryKiB.Value,
                Iterations = syncedPasswordRow.KdfIterations.Value,
                Parallelism = syncedPasswordRow.KdfParallelism.Value,
            };
            byte[] unlockedByPassword = VaultKeys.Unwrap(
                VaultKeys.DerivePasswordKey(kdf, Password, syncedPasswordRow.KdfSalt, syncedKdf),
                syncedPasswordRow.WrappedKey,
                VaultBinding.ForKeyRow(vault.Id, syncedPasswordRow.Id, (int)VaultKeyKind.Password));
            Assert.Equal(vaultKey, unlockedByPassword);

            VaultKey syncedRecoveryRow = deviceB.Vault.GetKey(vault.Id, VaultKeyKind.Recovery);
            Assert.True(RecoveryKeyFormat.TryDecode(RecoveryKeyFormat.Encode(recoveryKey), out byte[] typedRecovery));
            byte[] unlockedByRecovery = VaultKeys.Unwrap(
                VaultKeys.RecoveryWrappingKey(typedRecovery),
                syncedRecoveryRow.WrappedKey,
                VaultBinding.ForKeyRow(vault.Id, syncedRecoveryRow.Id, (int)VaultKeyKind.Recovery));
            Assert.Equal(vaultKey, unlockedByRecovery);

            VaultItem syncedItem = deviceB.VaultItems.Get(item.Id);
            Assert.Equal("Secret title", Encoding.UTF8.GetString(VaultCipher.Decrypt(unlockedByPassword, syncedItem.TitleEnc, VaultBinding.ForField(item.Id, "title", 1))));
            Assert.Equal("Secret body", Encoding.UTF8.GetString(VaultCipher.Decrypt(unlockedByPassword, syncedItem.BodyEnc, VaultBinding.ForField(item.Id, "body", 1))));

            VaultAttachment syncedAttachment = deviceB.VaultAttachments.Get(attachment.Id);
            Assert.Equal("photo.png", Encoding.UTF8.GetString(VaultCipher.Decrypt(unlockedByPassword, syncedAttachment.NameEnc, VaultBinding.ForField(attachment.Id, "name", 1))));
            byte[] syncedBlob = deviceB.VaultAttachments.GetBlob(attachment.Id);
            Assert.Equal(new byte[] { 9, 8, 7 }, VaultCipher.Decrypt(unlockedByPassword, syncedBlob, VaultBinding.ForField(attachment.Id, "blob", 1)));

            // Purging on A removes the rows, the blob, and the history on B.
            deviceA.VaultItems.PurgeSubtree(item.Id);
            await cycleA.Run();
            await cycleB.Run();

            Assert.Null(deviceB.VaultItems.Get(item.Id));
            Assert.Null(deviceB.VaultAttachments.Get(attachment.Id));
            Assert.Null(deviceB.VaultAttachments.GetBlob(attachment.Id));
            ChangeLogEntry remaining = Assert.Single(deviceB.ChangeLog.GetForItem(ItemTypes.VaultItem, item.Id));
            Assert.Equal(ChangeOps.Purge, remaining.Op);
            Assert.Empty(deviceA.ChangeLog.GetPending());
            Assert.Empty(deviceB.ChangeLog.GetPending());
        }

        private static SyncCycle CycleFor(TestDb db, ServerFixture server, HttpClient http)
        {
            db.SyncState.EnsureInitializedWithDevice(db.DeviceId);
            return new SyncCycle(db.ConnectionManager, db.DeviceId, new SyncServerClient(http, server.BaseUrl, ServerFixture.Token));
        }

        private static T Stamp<T>(T entity)
        {
            string now = Timestamps.UtcNowIso();
            switch (entity)
            {
                case Vault vault:
                    vault.CreatedAt = now;
                    vault.UpdatedAt = now;
                    break;
                case VaultKey key:
                    key.CreatedAt = now;
                    key.UpdatedAt = now;
                    break;
                case VaultItem item:
                    item.CreatedAt = now;
                    item.UpdatedAt = now;
                    break;
                case VaultAttachment attachment:
                    attachment.CreatedAt = now;
                    attachment.UpdatedAt = now;
                    break;
            }
            return entity;
        }

        private static ChangeLogEntry Foreign<T>(string itemType, string itemId, T payload, long? seq)
        {
            return new ChangeLogEntry
            {
                Seq = seq,
                ItemType = itemType,
                ItemId = itemId,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(payload),
                DeviceId = OtherDevice,
                ChangedAt = Timestamps.UtcNowIso(),
            };
        }

        private static ChangeLogEntry Purge(string itemType, string itemId, long seq)
        {
            return new ChangeLogEntry
            {
                Seq = seq,
                ItemType = itemType,
                ItemId = itemId,
                Op = ChangeOps.Purge,
                Payload = string.Empty,
                DeviceId = OtherDevice,
                ChangedAt = Timestamps.UtcNowIso(),
            };
        }
    }
}
