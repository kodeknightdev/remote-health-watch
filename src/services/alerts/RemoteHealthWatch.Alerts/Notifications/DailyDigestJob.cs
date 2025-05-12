using Microsoft.EntityFrameworkCore;
using Quartz;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Notifications;

/// <summary>Morning summary of the last 24h for care coordinators.</summary>
[DisallowConcurrentExecution]
public sealed class DailyDigestJob(AlertsDb db, StaffDirectory staff, IEmailSender email, TimeProvider clock, ILogger<DailyDigestJob> logger) : IJob
{
    public static readonly JobKey Key = new("daily-digest", "digests");

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var since = clock.GetUtcNow().AddDays(-1);
        var rows = await db.Alerts.AsNoTracking().Where(a => a.RaisedAt >= since)
            .Select(a => new { a.Severity, a.Status, a.EscalationLevel, a.RaisedAt, a.AcknowledgedAt }).ToListAsync(ct);
        var openUnacked = await db.Alerts.CountAsync(a => a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Acknowledged, ct);
        var acked = rows.Where(r => r.AcknowledgedAt is not null).ToList();
        var body = string.Join('\n',
            $"Alerts raised in the last 24h: {rows.Count}",
            $"  critical {rows.Count(r => r.Severity == AlertSeverity.Critical)}, high {rows.Count(r => r.Severity == AlertSeverity.High)}, " +
            $"medium {rows.Count(r => r.Severity == AlertSeverity.Medium)}, low/info {rows.Count(r => r.Severity <= AlertSeverity.Low)}",
            $"Escalated beyond the nurse: {rows.Count(r => r.EscalationLevel > 0)}",
            $"Average time to acknowledge: {(acked.Count == 0 ? "n/a" : $"{acked.Average(r => (r.AcknowledgedAt!.Value - r.RaisedAt).TotalMinutes):0.0} min")}",
            $"Currently unacknowledged: {openUnacked}");

        var coordinators = (await staff.AllAsync(ct)).Where(s => s.Role == Roles.CareCoordinator).ToList();
        foreach (var c in coordinators)
            await email.SendAsync(c.Email, $"remote-health-watch daily digest {clock.GetUtcNow():yyyy-MM-dd}", body, ct);
        logger.LogInformation("Daily digest sent to {Count} coordinators", coordinators.Count);
    }
}
