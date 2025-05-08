using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Messaging;

/// <summary>Cache invalidation for thresholds and care teams, and closing alerts on discharge.</summary>
public sealed class PatientHandler(CareTeamDirectory directory, AlertService alerts, EscalationManager escalation) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        if (envelope.Is<ThresholdsChanged>())
            await directory.InvalidateAsync(envelope.DataAs<ThresholdsChanged>().PatientId, ct);
        else if (envelope.Is<CareTeamChanged>())
            await directory.InvalidateAsync(envelope.DataAs<CareTeamChanged>().PatientId, ct);
        else if (envelope.Is<PatientDischarged>())
        {
            var patientId = envelope.DataAs<PatientDischarged>().PatientId;
            foreach (var id in await alerts.OpenAlertIdsAsync(patientId, null, ct))
                await escalation.ResolveAsync(id, null, "patient-discharged", ct);
        }
    }
}
