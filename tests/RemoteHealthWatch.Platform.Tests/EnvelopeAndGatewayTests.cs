using Microsoft.AspNetCore.Http;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.DeviceSim;
using RemoteHealthWatch.Gateway.RateLimiting;

namespace RemoteHealthWatch.Platform.Tests;

public class EnvelopeAndGatewayTests
{
    [Fact]
    public void Envelope_round_trips_with_string_enums_and_schema_version()
    {
        var e = new RiskScoreUpdated(Guid.NewGuid(), 7, News2Band.High, News2Band.Medium, new Dictionary<string, int> { ["Pulse"] = 2 }, DateTimeOffset.UtcNow);
        var json = EventEnvelope.Create(e, "risk-engine").Serialize();
        json.ShouldContain("\"band\":\"High\"");
        json.ShouldContain("\"schemaVersion\":1");

        var back = EventEnvelope.Deserialize(json);
        back.Is<RiskScoreUpdated>().ShouldBeTrue();
        back.DataAs<RiskScoreUpdated>().Score.ShouldBe(7);
    }

    [Fact]
    public void Raw_reading_events_carry_no_demographics()
    {
        var e = new ReadingsBatchReceived(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 50,
            [new RawReading(1, VitalType.HeartRate, 70, null, "bpm", DateTimeOffset.UtcNow)]);
        var json = EventEnvelope.Create(e, "devices").Serialize().ToLowerInvariant();
        foreach (var field in new[] { "name", "dateofbirth", "mrn", "address" }) json.ShouldNotContain($"\"{field}\"");
    }

    [Theory]
    [InlineData("rhwd_ab12cd34_secretsecret", "ab12cd34")]
    [InlineData("garbage", "no-key")]
    [InlineData("", "no-key")]
    public void Gateway_limits_devices_by_key_prefix_never_the_secret(string header, string partition)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Device-Key"] = header;
        GatewayRateLimits.DevicePartition(ctx).ShouldBe(partition);
    }

    [Fact]
    public void Sepsis_like_scenario_ends_clearly_abnormal()
    {
        var p = new Physiology(Scenario.Deteriorating, seed: 11, deteriorationTime: TimeSpan.FromMinutes(30));
        var start = p.Sample(TimeSpan.Zero);
        var end = p.Sample(TimeSpan.FromMinutes(31));
        end.HeartRate.ShouldBeGreaterThan(start.HeartRate + 30);
        end.RespiratoryRate.ShouldBeGreaterThan(24);
        end.SpO2.ShouldBeLessThan(start.SpO2 - 4);
        end.Temperature.ShouldBeGreaterThan(38.5);
    }

    [Fact]
    public void Copd_baseline_sits_below_the_normal_saturation_range()
    {
        var p = new Physiology(Scenario.Copd, seed: 3, deteriorationTime: TimeSpan.FromMinutes(30));
        Enumerable.Range(0, 50).Select(i => p.Sample(TimeSpan.FromMinutes(i)).SpO2).Average().ShouldBeInRange(87, 92);
    }
}
