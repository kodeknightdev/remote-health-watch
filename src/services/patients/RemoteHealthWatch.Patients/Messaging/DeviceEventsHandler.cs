using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.Patients.Services;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Patients.Messaging;

/// <summary>Enrollment saga steps driven by device events.</summary>
public sealed class DeviceEventsHandler(PatientsDb db, PatientService patients, TimeProvider clock, ILogger<DeviceEventsHandler> logger)
    : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var (patientId, step) = envelope.Type switch
        {
            var t when t == DevicePaired.EventType => (envelope.DataAs<DevicePaired>().PatientId, (Func<Patient, StatusTransition?>)
                (p => EnrollmentStateMachine.OnDevicePaired(p, clock.GetUtcNow()))),
            var t when t == DeviceUnpaired.EventType => (envelope.DataAs<DeviceUnpaired>().PatientId, EnrollmentStateMachine.OnDeviceUnpaired),
            var t when t == DevicePairingFailed.EventType => DataFor(envelope.DataAs<DevicePairingFailed>()),
            _ => (Guid.Empty, null)
        };
        if (step is null) return;

        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null)
        {
            logger.LogWarning("{EventType} for unknown patient {PatientId}", envelope.Type, patientId);
            return;
        }

        var transition = step(patient);
        patient.Version = Guid.NewGuid();
        if (transition is not null)
        {
            logger.LogInformation("Patient {PatientId} moved {From} -> {To} ({Reason})", patient.Id, transition.From, transition.To, transition.Reason);
            patients.PublishTransition(patient, transition);
        }
        await db.SaveChangesAsync(ct);
    }

    private static (Guid, Func<Patient, StatusTransition?>) DataFor(DevicePairingFailed e) =>
        (e.PatientId, p => EnrollmentStateMachine.OnPairingFailed(p, e.Reason));
}
