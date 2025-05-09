using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Escalation;

public abstract record EscalationCommand;
public sealed record NotifyRole(string Role, int Level, bool Final = false) : EscalationCommand;
public sealed record ScheduleTimeout(int Level, DateTimeOffset DueAt) : EscalationCommand;
public sealed record CancelTimeouts : EscalationCommand;
public sealed record AnnounceEscalation(AlertStatus From, AlertStatus To) : EscalationCommand;
public sealed record AnnounceAcknowledged(Guid ByUserId, double SecondsToAcknowledge) : EscalationCommand;
public sealed record AnnounceResolved(Guid? ByUserId, string Reason) : EscalationCommand;

public sealed record EscalationDecision(bool Changed, IReadOnlyList<EscalationCommand> Commands)
{
    public static readonly EscalationDecision NoOp = new(false, []);
}

/// <summary>
/// Raised → NotifiedNurse → EscalatedDoctor → EscalatedCoordinator, ending in Acknowledged or Resolved.
/// Each timeout carries the level it was scheduled for, so a replayed or late timer is a no-op.
/// </summary>
public static class EscalationStateMachine
{
    public const int Nurse = 0;
    public const int Doctor = 1;
    public const int Coordinator = 2;
    public const int Unanswered = 3;

    /// <summary>How long each level has to acknowledge before the next one is paged.</summary>
    public static IReadOnlyList<TimeSpan> Windows(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)],
        AlertSeverity.High => [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)],
        AlertSeverity.Medium => [TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)],
        AlertSeverity.Low => [TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(120)],
        _ => []
    };

    public static EscalationDecision Start(Alert alert, DateTimeOffset now)
    {
        if (alert.Status != AlertStatus.Raised || alert.AssignedRole != "") return EscalationDecision.NoOp;
        Add(alert, now, TimelineKind.Raised, alert.Message);

        // informational alerts (device paperwork) go straight to the coordinator and never escalate
        if (alert.Severity == AlertSeverity.Info)
        {
            alert.AssignedRole = Roles.CareCoordinator;
            alert.EscalationLevel = Coordinator;
            return new EscalationDecision(true, [new NotifyRole(Roles.CareCoordinator, Coordinator)]);
        }
        return PageNurse(alert, now, AlertStatus.Raised);
    }

    private static EscalationDecision PageNurse(Alert alert, DateTimeOffset now, AlertStatus from)
    {
        var window = Windows(alert.Severity)[Nurse];
        alert.Status = AlertStatus.NotifiedNurse;
        alert.EscalationLevel = Nurse;
        alert.AssignedRole = Roles.Nurse;
        alert.NextEscalationAt = now + window;
        Add(alert, now, TimelineKind.NotifiedNurse, $"ack window {window.TotalMinutes:0} min");
        return new EscalationDecision(true,
        [
            new NotifyRole(Roles.Nurse, Nurse),
            new ScheduleTimeout(Nurse, now + window),
            new AnnounceEscalation(from, AlertStatus.NotifiedNurse)
        ]);
    }

    public static EscalationDecision OnTimeout(Alert alert, int level, DateTimeOffset now)
    {
        if (!alert.IsAwaitingAcknowledgement || alert.EscalationLevel != level || alert.Severity == AlertSeverity.Info)
            return EscalationDecision.NoOp;

        var windows = Windows(alert.Severity);
        var from = alert.Status;
        switch (level)
        {
            case Nurse:
                return Move(alert, now, AlertStatus.EscalatedDoctor, Doctor, Roles.Doctor, now + windows[Doctor], TimelineKind.EscalatedDoctor, from);
            case Doctor:
                return Move(alert, now, AlertStatus.EscalatedCoordinator, Coordinator, Roles.CareCoordinator, now + windows[Coordinator],
                    TimelineKind.EscalatedCoordinator, from);
            case Coordinator:
                // nobody answered at any level: page everyone once more and stop the clock
                alert.EscalationLevel = Unanswered;
                alert.NextEscalationAt = null;
                Add(alert, now, TimelineKind.Unanswered, "no acknowledgement at any level");
                return new EscalationDecision(true,
                [
                    new NotifyRole(Roles.Nurse, Unanswered, Final: true),
                    new NotifyRole(Roles.Doctor, Unanswered, Final: true),
                    new NotifyRole(Roles.CareCoordinator, Unanswered, Final: true),
                    new AnnounceEscalation(from, alert.Status)
                ]);
            default:
                return EscalationDecision.NoOp;
        }
    }

    public static EscalationDecision Acknowledge(Alert alert, Guid userId, DateTimeOffset now)
    {
        if (!alert.IsAwaitingAcknowledgement) return EscalationDecision.NoOp;
        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = now;
        alert.AcknowledgedBy = userId;
        alert.NextEscalationAt = null;
        Add(alert, now, TimelineKind.Acknowledged, "", userId);
        return new EscalationDecision(true, [new CancelTimeouts(), new AnnounceAcknowledged(userId, (now - alert.RaisedAt).TotalSeconds)]);
    }

    public static EscalationDecision Resolve(Alert alert, Guid? userId, string reason, DateTimeOffset now)
    {
        if (alert.Status == AlertStatus.Resolved) return EscalationDecision.NoOp;
        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = now;
        alert.ResolvedBy = userId;
        alert.ResolutionReason = reason;
        alert.NextEscalationAt = null;
        Add(alert, now, TimelineKind.Resolved, reason, userId);
        return new EscalationDecision(true, [new CancelTimeouts(), new AnnounceResolved(userId, reason)]);
    }

    /// <summary>The same problem got worse: keep the alert, tighten the clock to the new severity at the current level.</summary>
    public static EscalationDecision Upgrade(Alert alert, AlertSeverity severity, string message, DateTimeOffset now)
    {
        if (severity <= alert.Severity || alert.Status == AlertStatus.Resolved) return EscalationDecision.NoOp;
        var previous = alert.Severity;
        alert.Severity = severity;
        alert.Message = message;
        Add(alert, now, TimelineKind.SeverityRaised, $"{previous} -> {severity}");

        if (previous == AlertSeverity.Info)
            return alert.IsAwaitingAcknowledgement ? PageNurse(alert, now, alert.Status) : new EscalationDecision(true, []);
        if (!alert.IsAwaitingAcknowledgement || alert.EscalationLevel >= Unanswered)
            return new EscalationDecision(true, []);

        var due = now + Windows(severity)[alert.EscalationLevel];
        if (alert.NextEscalationAt is { } current && current < due) due = current;
        alert.NextEscalationAt = due;
        return new EscalationDecision(true,
            [new CancelTimeouts(), new ScheduleTimeout(alert.EscalationLevel, due), new NotifyRole(alert.AssignedRole, alert.EscalationLevel)]);
    }

    private static EscalationDecision Move(Alert alert, DateTimeOffset now, AlertStatus to, int level, string role, DateTimeOffset due,
        TimelineKind kind, AlertStatus from)
    {
        alert.Status = to;
        alert.EscalationLevel = level;
        alert.AssignedRole = role;
        alert.NextEscalationAt = due;
        Add(alert, now, kind, $"no acknowledgement, paging {role}");
        return new EscalationDecision(true,
            [new NotifyRole(role, level), new ScheduleTimeout(level, due), new AnnounceEscalation(from, to)]);
    }

    private static void Add(Alert alert, DateTimeOffset at, TimelineKind kind, string detail, Guid? actor = null) =>
        alert.Timeline.Add(new AlertTimelineEntry { AlertId = alert.Id, At = at, Kind = kind, Detail = detail, ActorUserId = actor });
}
