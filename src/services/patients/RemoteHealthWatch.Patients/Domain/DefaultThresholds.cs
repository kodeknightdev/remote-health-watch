namespace RemoteHealthWatch.Patients.Domain;

public sealed record ThresholdValues(double? LowCritical, double? LowWarning, double? HighWarning, double? HighCritical);

/// <summary>Adult defaults, then adjustments by condition. Clinician overrides win over both.</summary>
public static class DefaultThresholds
{
    private static readonly Dictionary<string, ThresholdValues> Adult = new()
    {
        [Metrics.HeartRate] = new(40, 50, 110, 130),
        [Metrics.SpO2] = new(91, 94, null, null),
        [Metrics.RespiratoryRate] = new(8, 11, 21, 25),
        [Metrics.Temperature] = new(35.0, 36.0, 38.1, 39.1),
        [Metrics.Systolic] = new(90, 100, 160, 180),
        [Metrics.Diastolic] = new(null, 50, 100, 110),
        [Metrics.Glucose] = new(54, 70, 250, 350)
    };

    public static IReadOnlyDictionary<string, (ThresholdValues Values, ThresholdSource Source)> For(IEnumerable<string> conditions)
    {
        var result = Adult.ToDictionary(kv => kv.Key, kv => (kv.Value, ThresholdSource.Default));
        var set = conditions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // COPD target saturation is 88-92%, alerting at 94 would fire all day
        if (set.Contains(Conditions.Copd))
            result[Metrics.SpO2] = (new(84, 88, null, null), ThresholdSource.Condition);

        if (set.Contains(Conditions.Hypertension))
        {
            result[Metrics.Systolic] = (new(95, 105, 170, 190), ThresholdSource.Condition);
            result[Metrics.Diastolic] = (new(null, 55, 105, 120), ThresholdSource.Condition);
        }

        // beta blockers keep resting HR low in most of the heart failure cohort
        if (set.Contains(Conditions.HeartFailure))
        {
            result[Metrics.HeartRate] = (new(40, 45, 100, 120), ThresholdSource.Condition);
            result[Metrics.SpO2] = set.Contains(Conditions.Copd) ? result[Metrics.SpO2] : (new(90, 92, null, null), ThresholdSource.Condition);
        }

        if (set.Contains(Conditions.Diabetes))
            result[Metrics.Glucose] = (new(54, 70, 300, 400), ThresholdSource.Condition);

        return result;
    }
}
