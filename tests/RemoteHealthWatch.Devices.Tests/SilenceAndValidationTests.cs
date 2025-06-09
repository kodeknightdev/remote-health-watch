using Microsoft.Extensions.Time.Testing;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.Devices.Ingestion;

namespace RemoteHealthWatch.Devices.Tests;

public class SilenceAndValidationTests
{
    private static readonly DateTimeOffset Now = new(2025, 4, 23, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(5, 59, false)]
    [InlineData(5, 61, true)]
    [InlineData(300, 899, false)]
    [InlineData(300, 901, true)]
    public void Device_is_silent_after_three_missed_intervals_with_a_one_minute_floor(int interval, int secondsSinceSeen, bool silent)
    {
        var device = new Device { Status = DeviceStatus.Paired, ExpectedIntervalSeconds = interval, LastSeenAt = Now.AddSeconds(-secondsSinceSeen) };
        SilenceRule.IsSilent(device, Now).ShouldBe(silent);
    }

    [Fact]
    public void Already_flagged_or_unpaired_devices_are_not_flagged_again()
    {
        SilenceRule.IsSilent(new Device { Status = DeviceStatus.Paired, LastSeenAt = Now.AddHours(-1), SilentSince = Now.AddMinutes(-50) }, Now)
            .ShouldBeFalse();
        SilenceRule.IsSilent(new Device { Status = DeviceStatus.Available, LastSeenAt = Now.AddHours(-1) }, Now).ShouldBeFalse();
    }

    [Fact]
    public void Never_seen_device_counts_from_pairing()
    {
        SilenceRule.IsSilent(new Device { Status = DeviceStatus.Paired, ExpectedIntervalSeconds = 5, PairedAt = Now.AddMinutes(-2) }, Now)
            .ShouldBeTrue();
    }

    private static IngestBatchValidator Validator() => new(new FakeTimeProvider(Now));

    [Fact]
    public void Well_formed_batch_passes()
    {
        var batch = new IngestBatch(80, [
            new IngestReading(1, VitalType.HeartRate, 72, null, "bpm", Now),
            new IngestReading(2, VitalType.SpO2, 0.97, null, "fraction", Now),
            new IngestReading(3, VitalType.BloodPressure, 128, 82, "mmHg", Now)
        ]);
        Validator().Validate(batch).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Duplicate_seq_in_one_batch_unknown_unit_and_missing_diastolic_are_rejected()
    {
        var batch = new IngestBatch(null, [
            new IngestReading(1, VitalType.HeartRate, 72, null, "bpm", Now),
            new IngestReading(1, VitalType.Temperature, 37, null, "K", Now),
            new IngestReading(2, VitalType.BloodPressure, 128, null, "mmHg", Now)
        ]);
        var errors = Validator().Validate(batch).Errors.Select(e => e.ErrorMessage).ToList();
        errors.ShouldContain(e => e.Contains("unique"));
        errors.ShouldContain(e => e.Contains("not accepted"));
        errors.ShouldContain(e => e.Contains("diastolic"));
    }

    [Fact]
    public void Readings_from_the_future_are_rejected()
    {
        var batch = new IngestBatch(null, [new IngestReading(1, VitalType.HeartRate, 72, null, "bpm", Now.AddMinutes(10))]);
        Validator().Validate(batch).IsValid.ShouldBeFalse();
    }
}
