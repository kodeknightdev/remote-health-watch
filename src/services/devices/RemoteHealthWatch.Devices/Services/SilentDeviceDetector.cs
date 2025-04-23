using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Devices.Services;

/// <summary>Flags paired devices that stopped reporting and publishes devices.device-silent once per silence.</summary>
public sealed class SilentDeviceDetector(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SilentDeviceDetector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // another replica flagged the same device first
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Silent device sweep failed: {Error}", ex.Message);
            }
        }
    }

    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevicesDb>();
        var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
        var now = clock.GetUtcNow();
        var earliest = now - SilenceRule.Minimum;

        var candidates = await db.Devices
            .Where(d => d.Status == DeviceStatus.Paired && d.SilentSince == null && (d.LastSeenAt ?? d.PairedAt) < earliest)
            .Take(500).ToListAsync(ct);
        var silent = candidates.Where(d => SilenceRule.IsSilent(d, now)).ToList();
        foreach (var device in silent)
        {
            device.SilentSince = now;
            device.Version = Guid.NewGuid();
            outbox.Enqueue(Topics.DeviceSilent, device.PatientId!.Value, new DeviceSilent(device.Id, device.PatientId.Value,
                device.LastSeenAt ?? device.PairedAt!.Value, device.ExpectedIntervalSeconds, now));
        }
        if (silent.Count == 0) return 0;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Count} devices went silent", silent.Count);
        return silent.Count;
    }
}
