using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Messaging;

public sealed class DeviceHandler(AlertService alerts, EscalationManager escalation) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        if (envelope.Is<DeviceSilent>())
        {
            var e = envelope.DataAs<DeviceSilent>();
            var minutes = Math.Max(1, (int)(e.DetectedAt - e.LastSeenAt).TotalMinutes);
            await alerts.RaiseAsync(new AlertCandidate(e.PatientId, AlertType.DeviceSilent, AlertSeverity.Low, null, "Connectivity",
                null, $"Device {e.DeviceId.ToString()[..8]} silent for {minutes} min (expected every {e.ExpectedIntervalSeconds}s)"), ct);
        }
        else if (envelope.Is<DevicePaired>())
        {
            // a device coming back on (re-paired) closes the silence alert for that patient
            var e = envelope.DataAs<DevicePaired>();
            foreach (var id in await alerts.OpenAlertIdsAsync(e.PatientId, AlertType.DeviceSilent, ct))
                await escalation.ResolveAsync(id, null, "device-paired", ct);
        }
        else if (envelope.Is<DeviceUnpaired>())
        {
            var e = envelope.DataAs<DeviceUnpaired>();
            if (e.Reason is "patient-discharged") return;
            await alerts.RaiseAsync(new AlertCandidate(e.PatientId, AlertType.DeviceUnpaired, AlertSeverity.Info, null, null, null,
                $"Device {e.DeviceId.ToString()[..8]} unpaired ({e.Reason}); patient is back to pending device"), ct);
        }
        else if (envelope.Is<DevicePairingFailed>())
        {
            var e = envelope.DataAs<DevicePairingFailed>();
            await alerts.RaiseAsync(new AlertCandidate(e.PatientId, AlertType.PairingFailed, AlertSeverity.Info, null, null, null,
                $"Requested device {e.Serial} could not be paired: {e.Reason}"), ct);
        }
    }
}
