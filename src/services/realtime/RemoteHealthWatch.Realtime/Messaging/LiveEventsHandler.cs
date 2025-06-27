using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Hybrid;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Realtime.Hubs;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Realtime.Messaging;

/// <summary>Fans Kafka events out to the patient's group and the programme-wide group.</summary>
public sealed class LiveEventsHandler(IHubContext<LiveHub> hub, HybridCache cache, TimeProvider clock) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        switch (topic)
        {
            case Topics.ReadingsNormalized:
                {
                    var e = envelope.DataAs<ReadingsNormalized>();
                    // late readings are history; the tiles only show what's current
                    var points = e.Readings.Where(r => !r.Late).Select(r => new VitalPoint(r.Vital, r.Value, r.Value2, r.Unit, r.MeasuredAt)).ToList();
                    if (points.Count > 0) await SendAsync(e.PatientId, ClientMethods.Vitals, new VitalsPush(e.PatientId, points), envelope, ct);
                    break;
                }
            case Topics.RiskScoreUpdated:
                {
                    var e = envelope.DataAs<RiskScoreUpdated>();
                    await SendAsync(e.PatientId, ClientMethods.Risk, new RiskPush(e.PatientId, e.Score, e.Band, e.PreviousBand, e.Components, e.ComputedAt),
                        envelope, ct);
                    break;
                }
            case Topics.RiskAnomalyDetected:
                {
                    var e = envelope.DataAs<AnomalyDetected>();
                    await SendAsync(e.PatientId, ClientMethods.Insight,
                        new InsightPush("anomaly", e.PatientId, $"{e.Vital} {e.Value:0.#} ({e.Method})", e.MeasuredAt), envelope, ct);
                    break;
                }
            case Topics.RiskDeteriorationTrend:
                {
                    var e = envelope.DataAs<DeteriorationTrend>();
                    await SendAsync(e.PatientId, ClientMethods.Insight, new InsightPush("trend", e.PatientId,
                        $"RR {e.RespiratoryRateSlopePerHour:+0.0;-0.0}/h, SpO2 {e.SpO2SlopePerHour:+0.0;-0.0}%/h", e.DetectedAt), envelope, ct);
                    break;
                }
            case Topics.AlertRaised:
                {
                    var e = envelope.DataAs<AlertRaised>();
                    await SendAlertAsync(new AlertPush("raised", e.AlertId, e.PatientId, e.Severity, e.Type, AlertStatus.Raised, e.Message,
                        envelope.OccurredAt, clock.GetUtcNow()), envelope, ct);
                    break;
                }
            case Topics.AlertAcknowledged:
                {
                    var e = envelope.DataAs<AlertAcknowledged>();
                    await SendAlertAsync(new AlertPush("acknowledged", e.AlertId, e.PatientId, e.Severity, null, AlertStatus.Acknowledged, null,
                        envelope.OccurredAt, clock.GetUtcNow()), envelope, ct);
                    break;
                }
            case Topics.AlertEscalated:
                {
                    var e = envelope.DataAs<AlertEscalated>();
                    await SendAlertAsync(new AlertPush("escalated", e.AlertId, e.PatientId, e.Severity, null, e.To, null, envelope.OccurredAt,
                        clock.GetUtcNow()), envelope, ct);
                    break;
                }
            case Topics.AlertResolved:
                {
                    var e = envelope.DataAs<AlertResolved>();
                    await SendAlertAsync(new AlertPush("resolved", e.AlertId, e.PatientId, null, null, AlertStatus.Resolved, e.Reason,
                        envelope.OccurredAt, clock.GetUtcNow()), envelope, ct);
                    break;
                }
            case Topics.CareTeamChanged:
                // group membership is computed on connect; dropping the cache makes the next connect see the new team
                await cache.RemoveByTagAsync(CacheKeys.CareTeamTag, ct);
                break;
        }
    }

    private Task SendAlertAsync(AlertPush push, EventEnvelope envelope, CancellationToken ct) =>
        SendAsync(push.PatientId, ClientMethods.Alert, push, envelope, ct);

    private async Task SendAsync(Guid patientId, string method, object payload, EventEnvelope envelope, CancellationToken ct)
    {
        await hub.Clients.Groups(LiveGroups.Patient(patientId), LiveGroups.All).SendAsync(method, payload, ct);
        HubMetrics.Pushed.Add(1, new KeyValuePair<string, object?>("method", method));
        HubMetrics.PushLag.Record((clock.GetUtcNow() - envelope.OccurredAt).TotalMilliseconds,
            new KeyValuePair<string, object?>("method", method));
    }
}
