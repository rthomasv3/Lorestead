using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;

namespace Lorestead.Core.Sync
{
    // One full sync pass: drain the outbox, pull to the head, backfill missing
    // attachment blobs. A 410 mid-pull switches to the full-resync path: wipe
    // everything rebuildable (pending entries survive), pull from zero, then drain
    // whatever is still pending. The engine serializes calls - Run is not reentrant.
    public sealed class SyncCycle
    {
        private const int BatchLimit = 200;

        private readonly ConnectionManager _connectionManager;
        private readonly SyncServerClient _server;
        private readonly int _historyRetention;
        private readonly ChangeLogRepository _changeLog;
        private readonly SyncStateRepository _syncState;
        private readonly AttachmentRepository _attachments;
        private readonly ChangeApplier _applier;

        public SyncCycle(ConnectionManager connectionManager, string deviceId, SyncServerClient server, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _server = server;
            _historyRetention = historyRetention;
            _changeLog = new ChangeLogRepository(connectionManager);
            _syncState = new SyncStateRepository(connectionManager);
            _attachments = new AttachmentRepository(connectionManager, deviceId);
            _applier = new ChangeApplier(connectionManager, deviceId, historyRetention);
        }

        public async Task<SyncCycleResult> Run()
        {
            SyncCycleResult result = new SyncCycleResult();

            try
            {
                await Drain(result);
                await Pull(result);
            }
            catch (ResyncRequiredException)
            {
                new ResyncRepository(_connectionManager).WipeSyncedState();
                result.Resynced = true;
                await Pull(result);
                // Entries drained before the 410 were wiped with the mirror and come
                // back through the pull; only edits still pending upload here.
                await Drain(result);
            }

            await BackfillMissingBlobs(result);

            return result;
        }

        private async Task Drain(SyncCycleResult result)
        {
            List<PendingChange> pending = _changeLog.GetPending();

            for (int offset = 0; offset < pending.Count; offset += BatchLimit)
            {
                List<PendingChange> batch = pending.Skip(offset).Take(BatchLimit).ToList();
                UploadChangesResponse response = await _server.PostChanges(batch.Select(p => p.Entry).ToList());

                for (int i = 0; i < batch.Count; i++)
                {
                    _changeLog.AssignSeq(batch[i].LocalId, response.Results[i].Seq);
                }

                foreach (IGrouping<string, PendingChange> item in batch.GroupBy(p => p.Entry.ItemType + "\n" + p.Entry.ItemId))
                {
                    ChangeLogEntry entry = item.First().Entry;
                    _changeLog.PruneItemVersions(entry.ItemType, entry.ItemId, _historyRetention);
                }

                await UploadBlobs(batch, result);
                result.Uploaded += batch.Count;
            }
        }

        private async Task UploadBlobs(List<PendingChange> batch, SyncCycleResult result)
        {
            IEnumerable<string> attachmentIds = batch
                .Where(p => p.Entry.ItemType == ItemTypes.Attachment && p.Entry.Op == ChangeOps.Upsert)
                .Select(p => p.Entry.ItemId)
                .Distinct();

            foreach (string attachmentId in attachmentIds)
            {
                byte[] blob = _attachments.GetBlob(attachmentId);

                if (blob != null && await _server.PutBlob(attachmentId, blob))
                {
                    result.BlobsUploaded++;
                }
            }
        }

        private async Task Pull(SyncCycleResult result)
        {
            bool caughtUp = false;

            while (!caughtUp)
            {
                long since = _syncState.Get().LastSeenSeq;
                ChangesPageResponse page = await _server.GetChanges(since, BatchLimit);

                if (page.Entries.Count > 0)
                {
                    _applier.Apply(page.Entries);
                    result.Applied += page.Entries.Count;

                    foreach (ChangeLogEntry entry in page.Entries)
                    {
                        result.ChangedItemTypes.Add(entry.ItemType);
                    }
                }

                caughtUp = page.Entries.Count < BatchLimit;
            }
        }

        // Attachment metadata syncs as a normal item; the blob follows lazily. A 404
        // (origin device has not uploaded yet) retries naturally on the next cycle
        // because the blob is still missing locally.
        private async Task BackfillMissingBlobs(SyncCycleResult result)
        {
            foreach (string attachmentId in _attachments.GetIdsMissingBlob())
            {
                byte[] data = await _server.GetBlob(attachmentId);

                if (data != null)
                {
                    _attachments.SaveBlob(attachmentId, data);
                    result.BlobsDownloaded++;
                }
            }
        }
    }
}
