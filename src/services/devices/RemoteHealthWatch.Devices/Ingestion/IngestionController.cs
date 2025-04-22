using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Devices.Ingestion;

[ApiController]
[Route("api/ingest")]
[AllowAnonymous]
public sealed class IngestionController(
    DeviceAuthCache authCache,
    KafkaProducer producer,
    DevicesDb db,
    TimeProvider clock,
    ILogger<IngestionController> logger) : ControllerBase
{
    /// <summary>Device batch upload. Authenticated by the X-Device-Key header, rate limited per device.</summary>
    [HttpPost("readings")]
    [EnableRateLimiting(RateLimits.PerDevice)]
    [ProducesResponseType<IngestAccepted>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Readings(IngestBatch batch, CancellationToken ct)
    {
        var raw = Request.Headers[DeviceKeys.HeaderName].ToString();
        var prefix = DeviceKeys.PrefixOf(raw);
        var device = prefix is null ? null : await authCache.GetAsync(prefix, ct);
        var now = clock.GetUtcNow();

        var check = device is null ? KeyCheck.Invalid : DeviceKeys.Verify(device, raw, now);
        if (check is KeyCheck.Invalid or KeyCheck.Revoked)
        {
            IngestionMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason", check.ToString().ToLowerInvariant()));
            return Problem(check == KeyCheck.Revoked ? "Device key revoked" : "Unknown device key", statusCode: StatusCodes.Status401Unauthorized);
        }
        if (device!.Status != DeviceStatus.Paired || device.PatientId is not { } patientId)
        {
            IngestionMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason", "unpaired"));
            return Problem("Device is not paired with a patient", statusCode: StatusCodes.Status409Conflict);
        }
        if (check == KeyCheck.ValidPrevious)
            Response.Headers["X-Device-Key-Rotated"] = "true";

        var batchId = Guid.NewGuid();
        var payload = new ReadingsBatchReceived(batchId, device.Id, patientId, now, batch.BatteryPercent,
            batch.Readings.Select(r => new RawReading(r.Seq, r.Vital, r.Value, r.Value2, r.Unit, r.MeasuredAt)).ToList());
        var envelope = EventEnvelope.Create(payload, "devices", Activity.Current?.TraceId.ToString(), now, batchId);

        var started = Stopwatch.GetTimestamp();
        // keyed by patient so every reading for one patient lands on one partition, in order
        await producer.ProduceAsync(Topics.ReadingsRaw, patientId.ToString(), envelope.Serialize(),
            new Dictionary<string, string> { ["event-id"] = batchId.ToString(), ["event-type"] = envelope.Type }, ct);
        IngestionMetrics.PublishDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        IngestionMetrics.Batches.Add(1);
        IngestionMetrics.Readings.Add(batch.Readings.Count);

        await TouchAsync(device.Id, batch, now, ct);
        return Accepted(new IngestAccepted(batchId, batch.Readings.Count));
    }

    private async Task TouchAsync(Guid deviceId, IngestBatch batch, DateTimeOffset now, CancellationToken ct)
    {
        var maxSeq = batch.Readings.Max(r => r.Seq);
        try
        {
            var resumed = await db.Devices.Where(d => d.Id == deviceId && d.SilentSince != null).AnyAsync(ct);
            await db.Devices.Where(d => d.Id == deviceId).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.LastSeenAt, now)
                .SetProperty(d => d.BatteryPercent, d => batch.BatteryPercent ?? d.BatteryPercent)
                .SetProperty(d => d.LastSeq, d => d.LastSeq == null || d.LastSeq < maxSeq ? maxSeq : d.LastSeq)
                .SetProperty(d => d.SilentSince, (DateTimeOffset?)null), ct);
            if (resumed) logger.LogInformation("Device {DeviceId} is reporting again", deviceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // readings are already on Kafka; a missed last-seen update only delays silence detection recovery
            logger.LogWarning("Last-seen update failed for {DeviceId}: {Error}", deviceId, ex.Message);
        }
    }
}

public static class RateLimits
{
    public const string PerDevice = "per-device";
}
