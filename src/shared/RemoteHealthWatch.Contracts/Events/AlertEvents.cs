namespace RemoteHealthWatch.Contracts.Events;

public enum AlertSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public enum AlertType
{
    ThresholdBreach,
    News2BandChange,
    Anomaly,
    DeteriorationTrend,
    DeviceSilent,
    DeviceUnpaired,
    PairingFailed
}

public enum AlertStatus
{
    Raised,
    NotifiedNurse,
    EscalatedDoctor,
    EscalatedCoordinator,
    Acknowledged,
    Resolved
}

public sealed record AlertRaised(
    Guid AlertId,
    Guid PatientId,
    AlertType Type,
    AlertSeverity Severity,
    VitalType? Vital,
    string Message,
    DateTimeOffset RaisedAt) : IIntegrationEvent
{
    public static string EventType => "alerts.alert-raised";
    public static int SchemaVersion => 1;
}

public sealed record AlertAcknowledged(Guid AlertId, Guid PatientId, Guid ByUserId, AlertSeverity Severity, double SecondsToAcknowledge, DateTimeOffset At)
    : IIntegrationEvent
{
    public static string EventType => "alerts.acknowledged";
    public static int SchemaVersion => 1;
}

public sealed record AlertEscalated(Guid AlertId, Guid PatientId, AlertSeverity Severity, AlertStatus From, AlertStatus To, DateTimeOffset At)
    : IIntegrationEvent
{
    public static string EventType => "alerts.escalated";
    public static int SchemaVersion => 1;
}

public sealed record AlertResolved(Guid AlertId, Guid PatientId, Guid? ByUserId, string Reason, DateTimeOffset At) : IIntegrationEvent
{
    public static string EventType => "alerts.resolved";
    public static int SchemaVersion => 1;
}
