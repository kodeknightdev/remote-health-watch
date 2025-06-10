using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Vitals.Domain;

namespace RemoteHealthWatch.Vitals.Tests;

public class NormalizationAndDedupTests
{
    [Theory]
    [InlineData(VitalType.Temperature, 100.4, "F", 38.0, "C")]
    [InlineData(VitalType.Glucose, 5.5, "mmol/L", 99.1, "mg/dL")]
    [InlineData(VitalType.SpO2, 0.94, "fraction", 94.0, "%")]
    [InlineData(VitalType.SpO2, 0.97, "%", 97.0, "%")]
    [InlineData(VitalType.RespiratoryRate, 18, "brpm", 18, "/min")]
    [InlineData(VitalType.HeartRate, 72, "bpm", 72, "bpm")]
    public void Converts_to_canonical_units(VitalType vital, double value, string unit, double expected, string expectedUnit)
    {
        var n = UnitNormalizer.Normalize(vital, value, null, unit).ShouldNotBeNull();
        n.Value.ShouldBe(expected, 0.05);
        n.Unit.ShouldBe(expectedUnit);
    }

    [Fact]
    public void Blood_pressure_in_kpa_converts_both_values()
    {
        var n = UnitNormalizer.Normalize(VitalType.BloodPressure, 16, 10.7, "kPa").ShouldNotBeNull();
        n.Value.ShouldBe(120);
        n.Value2.ShouldBe(80);
    }

    [Theory]
    [InlineData(VitalType.HeartRate, 400, "bpm")]
    [InlineData(VitalType.SpO2, 30, "%")]
    [InlineData(VitalType.Temperature, 20, "C")]
    [InlineData(VitalType.HeartRate, 72, "furlongs")]
    public void Rejects_impossible_values_and_unknown_units(VitalType vital, double value, string unit) =>
        UnitNormalizer.Normalize(vital, value, null, unit).ShouldBeNull();

    [Fact]
    public void Diastolic_above_systolic_is_rejected()
    {
        UnitNormalizer.Normalize(VitalType.BloodPressure, 80, 120, "mmHg").ShouldBeNull();
    }

    [Fact]
    public void Resent_readings_after_reconnect_are_dropped()
    {
        var split = ReadingDeduplicator.Split(new long[] { 10, 11, 12, 13 }, s => s, new HashSet<long> { 10, 11 }, 11);
        split.Accepted.ShouldBe([12L, 13L]);
        split.Duplicates.ShouldBe([10L, 11L]);
        split.LateSeqs.ShouldBeEmpty();
    }

    [Fact]
    public void Older_unseen_readings_are_kept_but_flagged_late()
    {
        var split = ReadingDeduplicator.Split(new long[] { 5, 6, 21 }, s => s, new HashSet<long>(), 20);
        split.Accepted.ShouldBe([5L, 6L, 21L]);
        split.LateSeqs.ShouldBe([5L, 6L], ignoreOrder: true);
    }

    [Fact]
    public void Duplicate_inside_one_batch_counts_once()
    {
        var split = ReadingDeduplicator.Split(new long[] { 7, 7, 8 }, s => s, new HashSet<long>(), null);
        split.Accepted.ShouldBe([7L, 8L]);
        split.Duplicates.Count.ShouldBe(1);
    }

    [Fact]
    public void Buckets_group_by_minute_and_hour()
    {
        var at = new DateTimeOffset(2025, 4, 25, 10, 41, 37, TimeSpan.FromHours(1));
        Buckets.Start(at, RollupGrain.Minute).ShouldBe(new DateTime(2025, 4, 25, 9, 41, 0, DateTimeKind.Utc));
        Buckets.Start(at, RollupGrain.Hour).ShouldBe(new DateTime(2025, 4, 25, 9, 0, 0, DateTimeKind.Utc));
        Buckets.Touched([(VitalType.HeartRate, at), (VitalType.HeartRate, at.AddSeconds(5)), (VitalType.SpO2, at)], RollupGrain.Minute).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("24h", RollupGrain.Minute, 24)]
    [InlineData("7d", RollupGrain.Hour, 168)]
    [InlineData("30d", RollupGrain.Hour, 720)]
    public void Trend_ranges_pick_the_right_rollup(string range, RollupGrain grain, int hours)
    {
        TrendRanges.TryParse(range, out var parsed).ShouldBeTrue();
        TrendRanges.Resolve(parsed).ShouldBe((TimeSpan.FromHours(hours), grain));
    }
}
