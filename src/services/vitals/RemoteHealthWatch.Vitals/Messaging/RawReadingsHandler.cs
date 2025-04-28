using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.Vitals.Services;

namespace RemoteHealthWatch.Vitals.Messaging;

public sealed class RawReadingsHandler(ReadingProcessor processor, SnapshotStore snapshots, TimeProvider clock, ILogger<RawReadingsHandler> logger)
    : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var batch = envelope.DataAs<ReadingsBatchReceived>();
        var outcome = await processor.ProcessAsync(batch, ct);

        VitalsMetrics.Stored.Add(outcome.Stored);
        if (outcome.Duplicates > 0) VitalsMetrics.Duplicates.Add(outcome.Duplicates);
        if (outcome.Late > 0) VitalsMetrics.Late.Add(outcome.Late);
        if (outcome.Rejected > 0) VitalsMetrics.Rejected.Add(outcome.Rejected);
        var now = clock.GetUtcNow();
        foreach (var r in outcome.Readings.Where(r => !r.Late))
            VitalsMetrics.IngestionLag.Record((now - r.MeasuredAt).TotalMilliseconds);

        if (outcome.Duplicates > 0 || outcome.Rejected > 0)
            logger.LogDebug("Batch {BatchId} from device {DeviceId}: {Stored} stored, {Duplicates} duplicate, {Rejected} rejected",
                batch.BatchId, batch.DeviceId, outcome.Stored, outcome.Duplicates, outcome.Rejected);

        if (outcome.Readings.Count > 0)
            await snapshots.ApplyAsync(batch.PatientId, batch.DeviceId, outcome.Readings);
    }
}
