using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.Devices.Ingestion;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Devices.Services;

public sealed record PairResult(bool Ok, string? Error);

public sealed class DeviceService(DevicesDb db, IEventOutbox outbox, DeviceAuthCache authCache, TimeProvider clock)
{
    public async Task<(Device Device, string ApiKey)> RegisterAsync(string serial, DeviceType type, string firmware, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var key = DeviceKeys.Generate();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Serial = serial.Trim().ToUpperInvariant(),
            Type = type,
            Firmware = firmware,
            Status = DeviceStatus.Available,
            ApiKeyPrefix = key.Prefix,
            ApiKeyHash = key.Hash,
            KeyIssuedAt = now,
            RegisteredAt = now,
            Version = Guid.NewGuid()
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return (device, key.RawKey);
    }

    /// <summary>Pairs without an eligibility call; used by the enrollment saga where patients already vouched for the patient.</summary>
    public PairResult TryPair(Device device, Guid patientId, int intervalSeconds)
    {
        if (device.Status == DeviceStatus.Revoked) return new PairResult(false, "device-revoked");
        if (device.Status == DeviceStatus.Paired && device.PatientId != patientId) return new PairResult(false, "device-paired-elsewhere");
        if (device.Status == DeviceStatus.Paired) return new PairResult(true, null);

        var now = clock.GetUtcNow();
        device.Status = DeviceStatus.Paired;
        device.PatientId = patientId;
        device.PairedAt = now;
        device.ExpectedIntervalSeconds = intervalSeconds;
        device.SilentSince = null;
        device.LastSeq = null;
        device.Version = Guid.NewGuid();
        outbox.Enqueue(Topics.DevicePaired, patientId,
            new DevicePaired(device.Id, patientId, device.Type.ToString(), intervalSeconds, now));
        return new PairResult(true, null);
    }

    public void Unpair(Device device, string reason)
    {
        if (device.Status != DeviceStatus.Paired || device.PatientId is not { } patientId) return;
        device.Status = DeviceStatus.Available;
        device.PatientId = null;
        device.PairedAt = null;
        device.SilentSince = null;
        device.Version = Guid.NewGuid();
        outbox.Enqueue(Topics.DeviceUnpaired, patientId, new DeviceUnpaired(device.Id, patientId, reason, clock.GetUtcNow()));
    }

    public async Task<string> RotateKeyAsync(Device device, CancellationToken ct)
    {
        var oldPrefix = device.ApiKeyPrefix;
        var key = DeviceKeys.Rotate(device, clock.GetUtcNow());
        device.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await authCache.InvalidateAsync(oldPrefix, ct);
        return key.RawKey;
    }

    public async Task RevokeAsync(Device device, CancellationToken ct)
    {
        Unpair(device, "revoked");
        DeviceKeys.Revoke(device);
        device.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await authCache.InvalidateAsync(device.ApiKeyPrefix, ct);
    }

    public async Task SaveAsync(Device device, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await authCache.InvalidateAsync(device.ApiKeyPrefix, ct);
    }

    public Task<Device?> FindAsync(Guid id, CancellationToken ct) => db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
}
