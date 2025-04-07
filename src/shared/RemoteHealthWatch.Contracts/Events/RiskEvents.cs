namespace RemoteHealthWatch.Contracts.Events;

public enum News2Band
{
    Low,
    LowMedium,
    Medium,
    High
}

public sealed record RiskScoreUpdated(
    Guid PatientId,
    int Score,
    News2Band Band,
    News2Band? PreviousBand,
    IReadOnlyDictionary<string, int> Components,
    DateTimeOffset ComputedAt) : IIntegrationEvent
{
    public static string EventType => "risk.score-updated";
    public static int SchemaVersion => 1;
}

public sealed record AnomalyDetected(
    Guid PatientId,
    VitalType Vital,
    double Value,
    double? ZScore,
    string Method,
    DateTimeOffset MeasuredAt) : IIntegrationEvent
{
    public static string EventType => "risk.anomaly-detected";
    public static int SchemaVersion => 1;
}

public sealed record DeteriorationTrend(
    Guid PatientId,
    double RespiratoryRateSlopePerHour,
    double SpO2SlopePerHour,
    int WindowHours,
    DateTimeOffset DetectedAt) : IIntegrationEvent
{
    public static string EventType => "risk.deterioration-trend";
    public static int SchemaVersion => 1;
}
