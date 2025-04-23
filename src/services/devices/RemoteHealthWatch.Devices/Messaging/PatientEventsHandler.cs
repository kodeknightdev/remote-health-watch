using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.Devices.Services;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Devices.Messaging;

/// <summary>
/// Enrollment saga participant: pairs the device named at enrollment, or publishes pairing-failed so patients
/// can compensate. Discharge unpairs everything the patient still holds.
/// </summary>
public sealed class PatientEventsHandler(DevicesDb db, DeviceService devices, IEventOutbox outbox, TimeProvider clock,
    ILogger<PatientEventsHandler> logger) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        if (envelope.Is<PatientEnrolled>())
            await PairRequestedDeviceAsync(envelope.DataAs<PatientEnrolled>(), ct);
        else if (envelope.Is<PatientDischarged>())
            await UnpairAllAsync(envelope.DataAs<PatientDischarged>().PatientId, ct);
    }

    private async Task PairRequestedDeviceAsync(PatientEnrolled e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.RequestedDeviceSerial)) return;
        var serial = e.RequestedDeviceSerial.Trim().ToUpperInvariant();
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Serial == serial, ct);

        var result = device is null ? new PairResult(false, "unknown-serial") : devices.TryPair(device, e.PatientId, e.Plan.IntervalSeconds);
        if (!result.Ok)
        {
            logger.LogInformation("Requested device for patient {PatientId} could not be paired: {Reason}", e.PatientId, result.Error);
            outbox.Enqueue(Topics.DevicePairingFailed, e.PatientId, new DevicePairingFailed(e.PatientId, serial, result.Error!, clock.GetUtcNow()));
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task UnpairAllAsync(Guid patientId, CancellationToken ct)
    {
        var held = await db.Devices.Where(d => d.PatientId == patientId && d.Status == DeviceStatus.Paired).ToListAsync(ct);
        foreach (var device in held) devices.Unpair(device, "patient-discharged");
        await db.SaveChangesAsync(ct);
    }
}
