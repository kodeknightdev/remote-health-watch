using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Domain;

public sealed record ThresholdLimits(string Metric, double? LowCritical, double? LowWarning, double? HighWarning, double? HighCritical);

public enum BreachDirection
{
    Low,
    High
}

public sealed record Breach(string Metric, VitalType Vital, BreachDirection Direction, bool Critical, double Value, double Limit)
{
    public AlertSeverity Severity => Critical ? AlertSeverity.Critical : AlertSeverity.Medium;
}

public static class ThresholdEvaluator
{
    /// <summary>Maps a reading onto its metrics (BP splits into systolic/diastolic) and returns the worst breach per metric.</summary>
    public static IReadOnlyList<Breach> Evaluate(NormalizedReading reading, IReadOnlyDictionary<string, ThresholdLimits> limits)
    {
        var breaches = new List<Breach>();
        foreach (var (metric, value) in MetricsFor(reading))
        {
            if (!limits.TryGetValue(metric, out var l)) continue;
            var breach =
                l.LowCritical is { } lc && value <= lc ? new Breach(metric, reading.Vital, BreachDirection.Low, true, value, lc) :
                l.HighCritical is { } hc && value >= hc ? new Breach(metric, reading.Vital, BreachDirection.High, true, value, hc) :
                l.LowWarning is { } lw && value <= lw ? new Breach(metric, reading.Vital, BreachDirection.Low, false, value, lw) :
                l.HighWarning is { } hw && value >= hw ? new Breach(metric, reading.Vital, BreachDirection.High, false, value, hw) :
                null;
            if (breach is not null) breaches.Add(breach);
        }
        return breaches;
    }

    internal static IEnumerable<(string Metric, double Value)> MetricsFor(NormalizedReading r) => r.Vital switch
    {
        VitalType.BloodPressure => r.Value2 is { } dia ? [("Systolic", r.Value), ("Diastolic", dia)] : [("Systolic", r.Value)],
        _ => [(r.Vital.ToString(), r.Value)]
    };

    public static string Describe(Breach b) =>
        $"{b.Metric} {(b.Direction == BreachDirection.Low ? "below" : "above")} {(b.Critical ? "critical" : "warning")} limit " +
        $"({b.Value:0.#} vs {b.Limit:0.#})";
}
