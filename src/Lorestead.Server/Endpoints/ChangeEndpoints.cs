using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Sync;
using Lorestead.Server.Services;

namespace Lorestead.Server.Endpoints;

public static class ChangeEndpoints
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 500;

    public static void MapChangeEndpoints(this WebApplication app)
    {
        app.MapGet("/changes", (long? since, int? limit, ChangeLogRepository changeLog, ServerStateRepository serverState) =>
        {
            IResult result;
            long effectiveSince = Math.Max(since ?? 0, 0);

            // A cursor below the watermark predates a pruned purge entry - the gap is
            // unreplayable (the client would keep purged items), so it must resync.
            // since=0 is exempt: that is a full pull from a device with no prior
            // state (fresh install or post-wipe resync), which nothing can strand.
            if (effectiveSince > 0 && effectiveSince < serverState.GetPrunedThroughSeq())
            {
                result = Results.StatusCode(StatusCodes.Status410Gone);
            }
            else
            {
                result = Results.Ok(new ChangesPageResponse
                {
                    Entries = changeLog.GetAfter(effectiveSince, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit)),
                    MaxSeq = changeLog.GetMaxSeq(),
                });
            }

            return result;
        });

        app.MapPost("/changes", async (UploadChangesRequest request, ChangeIngestor ingestor, ChangeLogRepository changeLog, SyncHintBroadcaster broadcaster) =>
        {
            IResult result;

            try
            {
                UploadChangesResponse response = ingestor.Ingest(request.Entries);
                await broadcaster.Broadcast(changeLog.GetMaxSeq());
                result = Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                result = Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            return result;
        });
    }
}
