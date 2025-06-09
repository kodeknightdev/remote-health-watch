using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.Patients.Services;

namespace RemoteHealthWatch.Patients.Tests;

public class DefaultThresholdTests
{
    [Fact]
    public void Copd_lowers_the_spo2_target()
    {
        var copd = DefaultThresholds.For(["COPD"]);
        var adult = DefaultThresholds.For([]);
        copd[Metrics.SpO2].Values.LowWarning.ShouldBe(88);
        copd[Metrics.SpO2].Source.ShouldBe(ThresholdSource.Condition);
        adult[Metrics.SpO2].Values.LowWarning.ShouldBe(94);
    }

    [Fact]
    public void Hypertension_raises_blood_pressure_limits()
    {
        DefaultThresholds.For(["Hypertension"])[Metrics.Systolic].Values.HighWarning!.Value.ShouldBeGreaterThan(
            DefaultThresholds.For([])[Metrics.Systolic].Values.HighWarning!.Value);
    }

    [Fact]
    public void Copd_target_survives_heart_failure()
    {
        DefaultThresholds.For(["HeartFailure", "COPD"])[Metrics.SpO2].Values.LowWarning.ShouldBe(88);
    }

    [Fact]
    public void Every_metric_has_a_default()
    {
        DefaultThresholds.For(["Diabetes"]).Keys.ShouldBe(Metrics.All, ignoreOrder: true);
    }

    [Theory]
    [InlineData(84.0, 88.0, null, null, true)]
    [InlineData(90.0, 88.0, null, null, false)]
    [InlineData(40.0, 50.0, 110.0, 130.0, true)]
    [InlineData(40.0, 50.0, 45.0, 130.0, false)]
    public void Override_values_must_be_ordered(double? lc, double? lw, double? hw, double? hc, bool valid) =>
        UpdateThresholdsValidator.BeOrdered(new ThresholdDto("HeartRate", lc, lw, hw, hc, ThresholdSource.Override)).ShouldBe(valid);
}
