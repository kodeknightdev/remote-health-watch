using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Escalation;

/// <summary>
/// Safety net for the gap between committing an alert and scheduling its Quartz trigger: anything overdue by more
/// than the grace period gets its timeout applied directly. Safe to overlap with Quartz since timeouts are level-checked.
/// </summary>
public sealed class EscalationRecovery(IServiceScopeFactory scopes, TimeProvider clock, ILogger<EscalationRecovery> logger) : BackgroundService
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Escalation recovery sweep failed: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlertsDb>();
        var manager = scope.ServiceProvider.GetRequiredService<EscalationManager>();
        var overdue = clock.GetUtcNow() - Grace;
        var due = await db.Alerts.AsNoTracking()
            .Where(a => a.NextEscalationAt != null && a.NextEscalationAt < overdue
                        && a.Status != AlertStatus.Acknowledged && a.Status != AlertStatus.Resolved)
            .Select(a => new { a.Id, a.EscalationLevel }).Take(100).ToListAsync(ct);
        var applied = 0;
        foreach (var a in due)
            if (await manager.TimeoutAsync(a.Id, a.EscalationLevel, ct)) applied++;
        if (applied > 0) logger.LogWarning("Recovered {Count} overdue escalations", applied);
        return applied;
    }
}
