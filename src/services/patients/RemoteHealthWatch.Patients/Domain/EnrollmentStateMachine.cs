using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Patients.Domain;

public sealed record StatusTransition(MonitoringStatus From, MonitoringStatus To, string Reason);

/// <summary>
/// Choreographed enrollment: enrolled → device paired → baseline building (24h) → monitoring active.
/// Unpairing the last device or a failed pairing compensates back to PendingDevice.
/// </summary>
public static class EnrollmentStateMachine
{
    public static StatusTransition? OnDevicePaired(Patient patient, DateTimeOffset now)
    {
        patient.PairedDeviceCount++;
        patient.LastPairingError = null;
        if (patient.Status != MonitoringStatus.PendingDevice) return null;
        patient.BaselineStartedAt = now;
        return Move(patient, MonitoringStatus.BaselineBuilding, "device-paired");
    }

    public static StatusTransition? OnDeviceUnpaired(Patient patient)
    {
        patient.PairedDeviceCount = Math.Max(0, patient.PairedDeviceCount - 1);
        if (patient.PairedDeviceCount > 0 || patient.Status is MonitoringStatus.Discharged or MonitoringStatus.PendingDevice)
            return null;
        patient.BaselineStartedAt = null;
        patient.MonitoringActiveAt = null;
        return Move(patient, MonitoringStatus.PendingDevice, "device-unpaired");
    }

    public static StatusTransition? OnPairingFailed(Patient patient, string reason)
    {
        patient.LastPairingError = reason;
        if (patient.PairedDeviceCount > 0 || patient.Status is MonitoringStatus.Discharged or MonitoringStatus.PendingDevice)
            return null;
        patient.BaselineStartedAt = null;
        return Move(patient, MonitoringStatus.PendingDevice, "pairing-failed");
    }

    public static StatusTransition? OnBaselineElapsed(Patient patient, DateTimeOffset now, TimeSpan baselineWindow)
    {
        if (patient.Status != MonitoringStatus.BaselineBuilding || patient.BaselineStartedAt is null) return null;
        if (now - patient.BaselineStartedAt.Value < baselineWindow) return null;
        patient.MonitoringActiveAt = now;
        return Move(patient, MonitoringStatus.MonitoringActive, "baseline-complete");
    }

    public static StatusTransition? Discharge(Patient patient, DateTimeOffset now)
    {
        if (patient.Status == MonitoringStatus.Discharged) return null;
        patient.DischargedAt = now;
        return Move(patient, MonitoringStatus.Discharged, "discharged");
    }

    private static StatusTransition Move(Patient patient, MonitoringStatus to, string reason)
    {
        var transition = new StatusTransition(patient.Status, to, reason);
        patient.Status = to;
        return transition;
    }
}
