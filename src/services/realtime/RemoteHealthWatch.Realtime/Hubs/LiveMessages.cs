using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Realtime.Hubs;

public sealed record VitalPoint(VitalType Vital, double Value, double? Value2, string Unit, DateTimeOffset MeasuredAt);
public sealed record VitalsPush(Guid PatientId, IReadOnlyList<VitalPoint> Readings);
public sealed record RiskPush(Guid PatientId, int Score, News2Band Band, News2Band? PreviousBand, IReadOnlyDictionary<string, int> Components,
    DateTimeOffset ComputedAt);
public sealed record AlertPush(string Kind, Guid AlertId, Guid PatientId, AlertSeverity? Severity, AlertType? Type, AlertStatus? Status,
    string? Message, DateTimeOffset OccurredAt, DateTimeOffset PushedAt);
public sealed record InsightPush(string Kind, Guid PatientId, string Summary, DateTimeOffset At);

public static class ClientMethods
{
    public const string Vitals = "vitals";
    public const string Risk = "risk";
    public const string Alert = "alert";
    public const string Insight = "insight";
}
