using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.Vitals.Domain;

public enum RollupGrain
{
    Minute,
    Hour
}

public static class Buckets
{
    public static DateTime Start(DateTimeOffset at, RollupGrain grain)
    {
        var utc = at.UtcDateTime;
        return grain == RollupGrain.Minute
            ? new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc)
            : new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
    }

    public static TimeSpan Width(RollupGrain grain) => grain == RollupGrain.Minute ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);

    /// <summary>Distinct (vital, bucket) pairs touched by a batch; only those rollups are recomputed.</summary>
    public static IReadOnlyList<(VitalType Vital, DateTime Bucket)> Touched(IEnumerable<(VitalType Vital, DateTimeOffset At)> readings, RollupGrain grain) =>
        readings.Select(r => (r.Vital, Start(r.At, grain))).Distinct().ToList();
}

public enum TrendRange
{
    Day,
    Week,
    Month
}

public static class TrendRanges
{
    public static bool TryParse(string? value, out TrendRange range)
    {
        (var ok, range) = value?.ToLowerInvariant() switch
        {
            "24h" or "1d" => (true, TrendRange.Day),
            "7d" => (true, TrendRange.Week),
            "30d" => (true, TrendRange.Month),
            _ => (false, TrendRange.Day)
        };
        return ok;
    }

    public static (TimeSpan Span, RollupGrain Grain) Resolve(TrendRange range) => range switch
    {
        TrendRange.Day => (TimeSpan.FromHours(24), RollupGrain.Minute),
        TrendRange.Week => (TimeSpan.FromDays(7), RollupGrain.Hour),
        _ => (TimeSpan.FromDays(30), RollupGrain.Hour)
    };
}
