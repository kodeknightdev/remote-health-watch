using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Notifications;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Escalation;

/// <summary>
/// Process manager for one alert's escalation. State changes are committed with optimistic concurrency first,
/// side effects (timers, paging) run only for the change that won, so an ack racing a timeout never double-pages.
/// </summary>
public sealed class EscalationManager(
    AlertsDb db,
    IEventOutbox outbox,
    IEscalationScheduler scheduler,
    AlertNotifier notifier,
    PostCommitActions afterCommit,
    TimeProvider clock,
    ILogger<EscalationManager> logger)
{
    private const int MaxAttempts = 4;

    public Task<bool> TimeoutAsync(Guid alertId, int level, CancellationToken ct) =>
        ApplyAsync(alertId, a => EscalationStateMachine.OnTimeout(a, level, clock.GetUtcNow()), ct);

    public Task<bool> AcknowledgeAsync(Guid alertId, Guid userId, CancellationToken ct) =>
        ApplyAsync(alertId, a => EscalationStateMachine.Acknowledge(a, userId, clock.GetUtcNow()), ct);

    public Task<bool> ResolveAsync(Guid alertId, Guid? userId, string reason, CancellationToken ct) =>
        ApplyAsync(alertId, a => EscalationStateMachine.Resolve(a, userId, reason, clock.GetUtcNow()), ct);

    /// <summary>Runs the side effects of a decision that is already committed (new alerts, upgrades).</summary>
    public async Task ExecuteAsync(Alert alert, EscalationDecision decision, CancellationToken ct)
    {
        foreach (var command in decision.Commands)
        {
            switch (command)
            {
                case ScheduleTimeout s:
                    await scheduler.ScheduleAsync(alert.Id, s.Level, s.DueAt, ct);
                    break;
                case CancelTimeouts:
                    await scheduler.CancelAsync(alert.Id, ct);
                    break;
                case NotifyRole n:
                    await notifier.NotifyAsync(alert, n.Role, n.Final, ct);
                    break;
            }
        }
    }

    /// <summary>Stages the integration events a decision announces; call before SaveChanges.</summary>
    public void Stage(Alert alert, EscalationDecision decision)
    {
        var now = clock.GetUtcNow();
        foreach (var command in decision.Commands)
        {
            switch (command)
            {
                case AnnounceEscalation e when e.From != AlertStatus.Raised:
                    outbox.Enqueue(Topics.AlertEscalated, alert.PatientId, new AlertEscalated(alert.Id, alert.PatientId, alert.Severity, e.From, e.To, now));
                    AlertMetrics.Escalations.Add(1, new KeyValuePair<string, object?>("to", e.To.ToString()));
                    break;
                case AnnounceAcknowledged a:
                    outbox.Enqueue(Topics.AlertAcknowledged, alert.PatientId,
                        new AlertAcknowledged(alert.Id, alert.PatientId, a.ByUserId, alert.Severity, a.SecondsToAcknowledge, now));
                    AlertMetrics.TimeToAcknowledge.Record(a.SecondsToAcknowledge, new KeyValuePair<string, object?>("severity", alert.Severity.ToString()));
                    break;
                case AnnounceResolved r:
                    outbox.Enqueue(Topics.AlertResolved, alert.PatientId, new AlertResolved(alert.Id, alert.PatientId, r.ByUserId, r.Reason, now));
                    break;
            }
        }
    }

    private async Task<bool> ApplyAsync(Guid alertId, Func<Alert, EscalationDecision> decide, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var alert = await db.Alerts.SingleOrDefaultAsync(a => a.Id == alertId, ct);
            if (alert is null) return false;

            var decision = decide(alert);
            if (!decision.Changed) return false;

            alert.Version = Guid.NewGuid();
            Stage(alert, decision);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug("Alert {AlertId} changed underneath us, re-evaluating (attempt {Attempt})", alertId, attempt);
                continue;
            }
            // inside a consumer the outer transaction hasn't committed yet, so defer paging until it has
            if (db.Database.CurrentTransaction is not null) afterCommit.Add(c => ExecuteAsync(alert, decision, c));
            else await ExecuteAsync(alert, decision, ct);
            return true;
        }
    }
}
