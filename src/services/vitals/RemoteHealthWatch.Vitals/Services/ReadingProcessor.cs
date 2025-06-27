using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.Vitals.Data;
using RemoteHealthWatch.Vitals.Domain;

namespace RemoteHealthWatch.Vitals.Services;

public sealed record ProcessOutcome(int Stored, int Duplicates, int Late, int Rejected, IReadOnlyList<NormalizedReading> Readings);

/// <summary>Write side: normalise, dedupe on (deviceId, seq), append, refresh rollups, stage the normalized event.</summary>
public sealed class ReadingProcessor(VitalsDb db, RollupWriter rollups, IEventOutbox outbox, TimeProvider clock)
{
    public async Task<ProcessOutcome> ProcessAsync(ReadingsBatchReceived batch, CancellationToken ct)
    {
        var normalized = new List<(RawReading Raw, Normalized Value)>();
        var rejected = 0;
        foreach (var raw in batch.Readings)
        {
            var n = UnitNormalizer.Normalize(raw.Vital, raw.Value, raw.Value2, raw.Unit);
            if (n is null) rejected++;
            else normalized.Add((raw, n));
        }

        var seqs = normalized.Select(r => r.Raw.Seq).ToList();
        var stored = await db.ReadingKeys.Where(k => k.DeviceId == batch.DeviceId && seqs.Contains(k.Seq)).Select(k => k.Seq).ToListAsync(ct);
        var cursor = await db.DeviceCursors.SingleOrDefaultAsync(c => c.DeviceId == batch.DeviceId, ct);
        var split = ReadingDeduplicator.Split(normalized, r => r.Raw.Seq, stored.ToHashSet(), cursor?.HighestSeq);

        var now = clock.GetUtcNow();
        var accepted = split.Accepted.Select(r => new NormalizedReading(r.Raw.Seq, r.Raw.Vital, r.Value.Value, r.Value.Value2, r.Value.Unit,
            r.Raw.MeasuredAt, split.LateSeqs.Contains(r.Raw.Seq))).ToList();

        if (accepted.Count > 0)
        {
            db.Readings.AddRange(accepted.Select(r => new VitalReading
            {
                DeviceId = batch.DeviceId,
                Seq = r.Seq,
                PatientId = batch.PatientId,
                Vital = r.Vital,
                Value = r.Value,
                Value2 = r.Value2,
                MeasuredAt = r.MeasuredAt.UtcDateTime,
                ReceivedAt = batch.ReceivedAt.UtcDateTime,
                Late = r.Late
            }));
            db.ReadingKeys.AddRange(accepted.Select(r => new ReadingKey { DeviceId = batch.DeviceId, Seq = r.Seq, StoredAt = now.UtcDateTime }));

            var highest = accepted.Max(r => r.Seq);
            if (cursor is null)
                db.DeviceCursors.Add(new DeviceCursor { DeviceId = batch.DeviceId, PatientId = batch.PatientId, HighestSeq = highest, UpdatedAt = now.UtcDateTime });
            else if (highest > cursor.HighestSeq || cursor.PatientId != batch.PatientId)
            {
                cursor.HighestSeq = Math.Max(highest, cursor.HighestSeq);
                cursor.PatientId = batch.PatientId;
                cursor.UpdatedAt = now.UtcDateTime;
            }
            await db.SaveChangesAsync(ct);
            await rollups.RefreshAsync(batch.PatientId, accepted.Select(r => (r.Vital, r.MeasuredAt)).ToList(), ct);
            outbox.Enqueue(Topics.ReadingsNormalized, batch.PatientId, new ReadingsNormalized(batch.PatientId, batch.DeviceId, accepted, now));
            await db.SaveChangesAsync(ct);
        }

        return new ProcessOutcome(accepted.Count, split.Duplicates.Count, split.LateSeqs.Count, rejected, accepted);
    }
}
