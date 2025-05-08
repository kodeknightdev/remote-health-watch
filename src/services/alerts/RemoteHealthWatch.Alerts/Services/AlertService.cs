using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Services;

public sealed record RaiseResult(CandidateOutcome Outcome, Alert? Alert);

public sealed class AlertService(AlertsDb db, IEventOutbox outbox, EscalationManager escalation, PostCommitActions afterCommit,
    TimeProvider clock)
{
    /// <summary>Creates, merges, upgrades or suppresses. Paging and timers are deferred until the consumer's transaction commits.</summary>
    public async Task<RaiseResult> RaiseAsync(AlertCandidate candidate, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var key = candidate.DedupKey;
        var open = await db.Alerts.Include(a => a.Timeline).SingleOrDefaultAsync(a => a.DedupKey == key && a.Status != AlertStatus.Resolved, ct);
        var lastResolved = open is not null ? null : await db.Alerts.AsNoTracking()
            .Where(a => a.DedupKey == key && a.Status == AlertStatus.Resolved)
            .OrderByDescending(a => a.ResolvedAt).FirstOrDefaultAsync(ct);

        var outcome = AlertPolicy.Decide(candidate, open, lastResolved, now);
        var tags = new KeyValuePair<string, object?>[] { new("type", candidate.Type.ToString()), new("severity", candidate.Severity.ToString()) };
        switch (outcome)
        {
            case CandidateOutcome.Suppressed:
                AlertMetrics.Suppressed.Add(1, tags);
                return new RaiseResult(outcome, null);

            case CandidateOutcome.MergeIntoOpen:
                open!.OccurrenceCount++;
                open.LastOccurredAt = now;
                if (candidate.Value is not null) open.Value = candidate.Value;
                open.Version = Guid.NewGuid();
                await db.SaveChangesAsync(ct);
                AlertMetrics.Merged.Add(1, tags);
                return new RaiseResult(outcome, open);

            case CandidateOutcome.UpgradeOpen:
            {
                var decision = EscalationStateMachine.Upgrade(open!, candidate.Severity, candidate.Message, now);
                open!.OccurrenceCount++;
                open.LastOccurredAt = now;
                open.Value = candidate.Value ?? open.Value;
                open.Version = Guid.NewGuid();
                escalation.Stage(open, decision);
                outbox.Enqueue(Topics.AlertRaised, open.PatientId, RaisedEvent(open));
                await db.SaveChangesAsync(ct);
                afterCommit.Add(c => escalation.ExecuteAsync(open, decision, c));
                return new RaiseResult(outcome, open);
            }

            default:
            {
                var alert = new Alert
                {
                    Id = Guid.NewGuid(),
                    PatientId = candidate.PatientId,
                    Type = candidate.Type,
                    Severity = candidate.Severity,
                    Vital = candidate.Vital,
                    Metric = candidate.Metric,
                    Value = candidate.Value,
                    Message = candidate.Message.Length > 300 ? candidate.Message[..300] : candidate.Message,
                    DedupKey = key,
                    Status = AlertStatus.Raised,
                    RaisedAt = now,
                    LastOccurredAt = now,
                    Version = Guid.NewGuid()
                };
                var decision = EscalationStateMachine.Start(alert, now);
                db.Alerts.Add(alert);
                outbox.Enqueue(Topics.AlertRaised, alert.PatientId, RaisedEvent(alert));
                escalation.Stage(alert, decision);
                await db.SaveChangesAsync(ct);
                AlertMetrics.Raised.Add(1, tags);
                afterCommit.Add(c => escalation.ExecuteAsync(alert, decision, c));
                return new RaiseResult(outcome, alert);
            }
        }
    }

    /// <summary>Resolves every open alert matching the filter, e.g. on discharge or when NEWS2 drops back to low.</summary>
    public async Task<IReadOnlyList<Guid>> OpenAlertIdsAsync(Guid patientId, AlertType? type, CancellationToken ct)
    {
        var query = db.Alerts.Where(a => a.PatientId == patientId && a.Status != AlertStatus.Resolved);
        if (type is not null) query = query.Where(a => a.Type == type);
        return await query.Select(a => a.Id).ToListAsync(ct);
    }

    private static AlertRaised RaisedEvent(Alert a) => new(a.Id, a.PatientId, a.Type, a.Severity, a.Vital, a.Message, a.RaisedAt);
}
