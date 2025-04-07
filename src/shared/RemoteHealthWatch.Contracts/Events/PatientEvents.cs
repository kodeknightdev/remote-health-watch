namespace RemoteHealthWatch.Contracts.Events;

public enum MonitoringStatus
{
    PendingDevice,
    BaselineBuilding,
    MonitoringActive,
    Discharged
}

public sealed record MonitoringPlanDto(IReadOnlyList<VitalType> Vitals, int IntervalSeconds);

public sealed record PatientEnrolled(
    Guid PatientId,
    IReadOnlyList<string> Conditions,
    MonitoringPlanDto Plan,
    int SpO2Scale,
    bool OnSupplementalOxygen,
    string? RequestedDeviceSerial,
    DateTimeOffset EnrolledAt) : IIntegrationEvent
{
    public static string EventType => "patients.patient-enrolled";
    public static int SchemaVersion => 1;
}

public sealed record ThresholdsChanged(Guid PatientId, DateTimeOffset ChangedAt) : IIntegrationEvent
{
    public static string EventType => "patients.thresholds-changed";
    public static int SchemaVersion => 1;
}

public sealed record CareTeamMemberDto(Guid UserId, string Role);

public sealed record CareTeamChanged(Guid PatientId, IReadOnlyList<CareTeamMemberDto> Members, DateTimeOffset ChangedAt) : IIntegrationEvent
{
    public static string EventType => "patients.care-team-changed";
    public static int SchemaVersion => 1;
}

public sealed record PatientDischarged(Guid PatientId, DateTimeOffset DischargedAt) : IIntegrationEvent
{
    public static string EventType => "patients.patient-discharged";
    public static int SchemaVersion => 1;
}

public sealed record MonitoringStatusChanged(Guid PatientId, MonitoringStatus From, MonitoringStatus To, string Reason, DateTimeOffset ChangedAt)
    : IIntegrationEvent
{
    public static string EventType => "patients.monitoring-status-changed";
    public static int SchemaVersion => 1;
}
