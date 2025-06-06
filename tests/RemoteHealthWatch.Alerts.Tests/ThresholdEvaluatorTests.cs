using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Tests;

public class ThresholdEvaluatorTests
{
    private static readonly Dictionary<string, ThresholdLimits> Copd = new()
    {
        ["SpO2"] = new("SpO2", 84, 88, null, null),
        ["HeartRate"] = new("HeartRate", 40, 50, 110, 130),
        ["Systolic"] = new("Systolic", 90, 100, 160, 180),
        ["Diastolic"] = new("Diastolic", null, 50, 100, 110)
    };

    private static NormalizedReading Reading(VitalType vital, double value, double? value2 = null) =>
        new(1, vital, value, value2, "", DateTimeOffset.UtcNow, false);

    [Fact]
    public void Copd_patient_at_89_percent_is_within_personal_limits()
    {
        ThresholdEvaluator.Evaluate(Reading(VitalType.SpO2, 89), Copd).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(87, false)]
    [InlineData(84, true)]
    [InlineData(80, true)]
    public void SpO2_below_limits_breaches_low(double value, bool critical)
    {
        var breach = ThresholdEvaluator.Evaluate(Reading(VitalType.SpO2, value), Copd).ShouldHaveSingleItem();
        breach.Direction.ShouldBe(BreachDirection.Low);
        breach.Critical.ShouldBe(critical);
        breach.Severity.ShouldBe(critical ? AlertSeverity.Critical : AlertSeverity.Medium);
    }

    [Fact]
    public void Critical_wins_over_warning_on_the_same_side()
    {
        var breach = ThresholdEvaluator.Evaluate(Reading(VitalType.HeartRate, 135), Copd).ShouldHaveSingleItem();
        breach.Critical.ShouldBeTrue();
        breach.Limit.ShouldBe(130);
    }

    [Fact]
    public void Blood_pressure_is_checked_as_systolic_and_diastolic()
    {
        var breaches = ThresholdEvaluator.Evaluate(Reading(VitalType.BloodPressure, 185, 104), Copd);
        breaches.Select(b => (b.Metric, b.Critical)).ShouldBe([("Systolic", true), ("Diastolic", false)], ignoreOrder: true);
    }

    [Fact]
    public void Missing_limits_for_a_metric_are_ignored()
    {
        ThresholdEvaluator.Evaluate(Reading(VitalType.Glucose, 500), Copd).ShouldBeEmpty();
    }
}
