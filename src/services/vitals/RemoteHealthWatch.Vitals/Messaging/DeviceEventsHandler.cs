using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.Vitals.Data;

namespace RemoteHealthWatch.Vitals.Messaging;

/// <summary>Keeps a small device-status read model so trend views can show which device produced what.</summary>
public sealed class DeviceEventsHandler(VitalsDb db, TimeProvider clock) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (envelope.Is<DevicePaired>())
        {
            var e = envelope.DataAs<DevicePaired>();
            var state = await GetOrAddAsync(e.DeviceId, ct);
            state.PatientId = e.PatientId;
            state.DeviceType = e.DeviceType;
            state.Paired = true;
            state.ExpectedIntervalSeconds = e.ExpectedIntervalSeconds;
            state.SilentSince = null;
            state.UpdatedAt = now;
        }
        else if (envelope.Is<DeviceUnpaired>())
        {
            var e = envelope.DataAs<DeviceUnpaired>();
            var state = await GetOrAddAsync(e.DeviceId, ct);
            state.PatientId = e.PatientId;
            state.Paired = false;
            state.UpdatedAt = now;
        }
        else if (envelope.Is<DeviceSilent>())
        {
            var e = envelope.DataAs<DeviceSilent>();
            var state = await GetOrAddAsync(e.DeviceId, ct);
            state.PatientId = e.PatientId;
            state.SilentSince = e.DetectedAt.UtcDateTime;
            state.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<DeviceState> GetOrAddAsync(Guid deviceId, CancellationToken ct)
    {
        var state = await db.DeviceStates.SingleOrDefaultAsync(s => s.DeviceId == deviceId, ct);
        if (state is not null) return state;
        state = new DeviceState { DeviceId = deviceId };
        db.DeviceStates.Add(state);
        return state;
    }
}
