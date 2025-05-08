using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Messaging;

public sealed class RiskHandler(AlertService alerts, EscalationManager escalation) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        if (envelope.Is<RiskScoreUpdated>())
        {
            var e = envelope.DataAs<RiskScoreUpdated>();
            if (e.Band == News2Band.Low)
            {
                // score came back down; close the band alert rather than leave it for someone to tidy up
                if (e.PreviousBand is not null and not News2Band.Low)
                    foreach (var id in await alerts.OpenAlertIdsAsync(e.PatientId, AlertType.News2BandChange, ct))
                        await escalation.ResolveAsync(id, null, "news2-back-to-low", ct);
                return;
            }
            if (e.PreviousBand is { } previous && previous >= e.Band) return;
            await alerts.RaiseAsync(new AlertCandidate(e.PatientId, AlertType.News2BandChange, AlertPolicy.ForNews2(e.Band), null, "NEWS2",
                e.Score, $"NEWS2 {e.Score} ({e.Band}){(e.PreviousBand is null ? "" : $", up from {e.PreviousBand}")}"), ct);
        }
        else if (envelope.Is<AnomalyDetected>())
        {
            var e = envelope.DataAs<AnomalyDetected>();
            var z = e.ZScore is { } score ? $", z={score:0.0}" : "";
            await alerts.RaiseAsync(new AlertCandidate(e.PatientId, AlertType.Anomaly, AlertSeverity.Medium, e.Vital, e.Vital.ToString(),
                e.Value, $"{e.Vital} {e.Value:0.#} unusual for this patient ({e.Method}{z})"), ct);
        }
        else if (envelope.Is<DeteriorationTrend>())
        {
            var e = envelope.DataAs<DeteriorationTrend>();
            await alerts.RaiseAsync(new AlertCandidate(e.PatientId, AlertType.DeteriorationTrend, AlertSeverity.High, null, "Trend", null,
                $"Steady deterioration over {e.WindowHours}h: RR {e.RespiratoryRateSlopePerHour:+0.0;-0.0}/h, SpO2 {e.SpO2SlopePerHour:+0.0;-0.0}%/h"), ct);
        }
    }
}
