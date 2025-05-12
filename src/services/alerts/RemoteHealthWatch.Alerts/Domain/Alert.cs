using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Domain;

public class Alert
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public VitalType? Vital { get; set; }
    public string? Metric { get; set; }
    public double? Value { get; set; }
    public string Message { get; set; } = "";
    public string DedupKey { get; set; } = "";
    public AlertStatus Status { get; set; }

    /// <summary>0 nurse, 1 doctor, 2 coordinator, 3 unanswered after the coordinator window.</summary>
    public int EscalationLevel { get; set; }
    public string AssignedRole { get; set; } = "";
    public DateTimeOffset? NextEscalationAt { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset LastOccurredAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionReason { get; set; }
    public Guid Version { get; set; }

    public List<AlertTimelineEntry> Timeline { get; set; } = [];
    public List<AlertNote> Notes { get; set; } = [];

    public bool IsOpen => Status is not (AlertStatus.Resolved);
    public bool IsAwaitingAcknowledgement => Status is AlertStatus.Raised or AlertStatus.NotifiedNurse
        or AlertStatus.EscalatedDoctor or AlertStatus.EscalatedCoordinator;
}

public enum TimelineKind
{
    Raised,
    NotifiedNurse,
    EscalatedDoctor,
    EscalatedCoordinator,
    Unanswered,
    SeverityRaised,
    Acknowledged,
    Resolved,
    Note
}

public class AlertTimelineEntry
{
    public long Id { get; set; }
    public Guid AlertId { get; set; }
    public DateTimeOffset At { get; set; }
    public TimelineKind Kind { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Detail { get; set; } = "";
}

public class AlertNote
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
}
