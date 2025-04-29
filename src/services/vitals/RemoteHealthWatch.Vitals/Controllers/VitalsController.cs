using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.Vitals.Data;
using RemoteHealthWatch.Vitals.Domain;
using RemoteHealthWatch.Vitals.Services;

namespace RemoteHealthWatch.Vitals.Controllers;

public sealed record TrendPoint(DateTime T, double Avg, double Min, double Max, double? Avg2, int Count);
public sealed record TrendResponse(Guid PatientId, VitalType Vital, string Range, string Grain, string Unit, IReadOnlyList<TrendPoint> Points);
public sealed record VitalSummary(VitalType Vital, int Count, double Min, double Max, double Avg, double? Avg2, string Unit);
public sealed record SummaryResponse(Guid PatientId, int Hours, IReadOnlyList<VitalSummary> Vitals, DateTimeOffset? LastReadingAt);
public sealed record RawReadingDto(Guid DeviceId, long Seq, VitalType Vital, double Value, double? Value2, DateTime MeasuredAt, bool Late);
public sealed record ThroughputResponse(int WindowSeconds, long Readings, double ReadingsPerSecond, int ActivePatients);

[ApiController]
[Route("api/vitals")]
[Authorize(Policy = Policies.Staff)]
public sealed class VitalsController(VitalsDb db, SnapshotStore snapshots, TimeProvider clock) : ControllerBase
{
    [HttpGet("{patientId:guid}/latest")]
    [PatientData("vitals.latest")]
    public async Task<ActionResult<IReadOnlyList<LatestValue>>> Latest(Guid patientId, CancellationToken ct)
    {
        var latest = await snapshots.GetAsync(patientId);
        if (latest.Count > 0) return Ok(latest);

        // cold cache: rebuild from the last day of raw readings
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-1);
        var rows = await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.MeasuredAt >= since)
            .GroupBy(r => r.Vital)
            .Select(g => g.OrderByDescending(r => r.MeasuredAt).First())
            .ToListAsync(ct);
        return rows.Select(r => new LatestValue(r.Vital, r.Value, r.Value2, CanonicalUnits.For(r.Vital),
            new DateTimeOffset(r.MeasuredAt, TimeSpan.Zero), r.DeviceId)).ToList();
    }

    [HttpGet("{patientId:guid}/trends")]
    [PatientData("vitals.trends")]
    public async Task<ActionResult<TrendResponse>> Trends(Guid patientId, [FromQuery] VitalType vital, [FromQuery] string range = "24h",
        CancellationToken ct = default)
    {
        if (!TrendRanges.TryParse(range, out var parsed)) return Problem("range must be 24h, 7d or 30d", statusCode: 400);
        var (span, grain) = TrendRanges.Resolve(parsed);
        var from = clock.GetUtcNow().UtcDateTime - span;

        IQueryable<Rollup> source = grain == RollupGrain.Minute ? db.MinuteRollups : db.HourRollups;
        var points = await source.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.Vital == vital && r.BucketStart >= from)
            .OrderBy(r => r.BucketStart)
            .Select(r => new TrendPoint(r.BucketStart, r.AvgValue, r.MinValue, r.MaxValue, r.AvgValue2, r.Count))
            .ToListAsync(ct);
        return new TrendResponse(patientId, vital, range, grain == RollupGrain.Minute ? "1m" : "1h", CanonicalUnits.For(vital), points);
    }

    [HttpGet("{patientId:guid}/summary")]
    [PatientData("vitals.summary")]
    public async Task<ActionResult<SummaryResponse>> Summary(Guid patientId, [FromQuery] int hours = 24, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 1, 24 * 30);
        var from = clock.GetUtcNow().UtcDateTime.AddHours(-hours);
        var rows = await db.HourRollups.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.BucketStart >= from)
            .ToListAsync(ct);
        var vitals = rows.GroupBy(r => r.Vital).Select(g =>
        {
            var count = g.Sum(r => r.Count);
            var avg2Rows = g.Where(r => r.AvgValue2 is not null).ToList();
            return new VitalSummary(g.Key, count, g.Min(r => r.MinValue), g.Max(r => r.MaxValue),
                Math.Round(g.Sum(r => r.AvgValue * r.Count) / count, 2),
                avg2Rows.Count == 0 ? null : Math.Round(avg2Rows.Sum(r => r.AvgValue2!.Value * r.Count) / avg2Rows.Sum(r => r.Count), 2),
                CanonicalUnits.For(g.Key));
        }).OrderBy(v => v.Vital).ToList();
        var latest = await snapshots.GetAsync(patientId);
        return new SummaryResponse(patientId, hours, vitals, latest.Count == 0 ? null : latest.Max(l => l.MeasuredAt));
    }

    [HttpGet("{patientId:guid}/readings")]
    [PatientData("vitals.readings")]
    public async Task<ActionResult<IReadOnlyList<RawReadingDto>>> Readings(Guid patientId, [FromQuery] VitalType? vital,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var end = to ?? clock.GetUtcNow().UtcDateTime;
        var start = from ?? end.AddHours(-1);
        var query = db.Readings.AsNoTracking().Where(r => r.PatientId == patientId && r.MeasuredAt >= start && r.MeasuredAt < end);
        if (vital is not null) query = query.Where(r => r.Vital == vital);
        return await query.OrderByDescending(r => r.MeasuredAt).Take(2000)
            .Select(r => new RawReadingDto(r.DeviceId, r.Seq, r.Vital, r.Value, r.Value2, r.MeasuredAt, r.Late))
            .ToListAsync(ct);
    }

    [HttpGet("{patientId:guid}/devices")]
    [PatientData("vitals.devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceState>>> Devices(Guid patientId, CancellationToken ct) =>
        await db.DeviceStates.AsNoTracking().Where(d => d.PatientId == patientId).ToListAsync(ct);

    /// <summary>Stored readings per second over a recent window, from the minute rollups.</summary>
    [HttpGet("stats/throughput")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<ThroughputResponse>> Throughput([FromQuery] int minutes = 5, CancellationToken ct = default)
    {
        minutes = Math.Clamp(minutes, 1, 60);
        // skip the current, still-filling minute
        var to = Buckets.Start(clock.GetUtcNow(), RollupGrain.Minute);
        var from = to.AddMinutes(-minutes);
        var rows = await db.MinuteRollups.AsNoTracking().Where(r => r.BucketStart >= from && r.BucketStart < to)
            .GroupBy(_ => 1).Select(g => new { Count = g.Sum(r => (long)r.Count), Patients = g.Select(r => r.PatientId).Distinct().Count() })
            .SingleOrDefaultAsync(ct);
        var total = rows?.Count ?? 0;
        return new ThroughputResponse(minutes * 60, total, Math.Round(total / (minutes * 60.0), 2), rows?.Patients ?? 0);
    }
}
