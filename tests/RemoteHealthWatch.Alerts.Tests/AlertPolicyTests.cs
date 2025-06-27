using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Tests;

public class AlertPolicyTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Patient = Guid.Parse("5b4e8d6c-1a2b-4c3d-8e9f-0a1b2c3d4e5f");

    private static AlertCandidate Candidate(AlertSeverity severity) =>
        new(Patient, AlertType.ThresholdBreach, severity, VitalType.SpO2, "SpO2", 85, "SpO2 low");

    private static Alert Existing(AlertSeverity severity, AlertStatus status, DateTimeOffset? resolvedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        PatientId = Patient,
        Severity = severity,
        Status = status,
        ResolvedAt = resolvedAt
    };

    [Fact]
    public void Same_problem_while_open_merges_instead_of_raising_again()
    {
        AlertPolicy.Decide(Candidate(AlertSeverity.Medium), Existing(AlertSeverity.Medium, AlertStatus.NotifiedNurse), null, Now)
            .ShouldBe(CandidateOutcome.MergeIntoOpen);
    }

    [Fact]
    public void Worse_reading_upgrades_the_open_alert()
    {
        AlertPolicy.Decide(Candidate(AlertSeverity.Critical), Existing(AlertSeverity.Medium, AlertStatus.Acknowledged), null, Now)
            .ShouldBe(CandidateOutcome.UpgradeOpen);
    }

    [Fact]
    public void Recently_resolved_problem_is_suppressed()
    {
        var resolved = Existing(AlertSeverity.Medium, AlertStatus.Resolved, Now.AddMinutes(-10));
        AlertPolicy.Decide(Candidate(AlertSeverity.Medium), null, resolved, Now).ShouldBe(CandidateOutcome.Suppressed);
    }

    [Fact]
    public void Suppression_window_expires()
    {
        var resolved = Existing(AlertSeverity.Medium, AlertStatus.Resolved, Now - AlertPolicy.SuppressionWindow(AlertSeverity.Medium));
        AlertPolicy.Decide(Candidate(AlertSeverity.Medium), null, resolved, Now).ShouldBe(CandidateOutcome.Create);
    }

    [Fact]
    public void Higher_severity_breaks_through_suppression()
    {
        var resolved = Existing(AlertSeverity.Medium, AlertStatus.Resolved, Now.AddMinutes(-1));
        AlertPolicy.Decide(Candidate(AlertSeverity.Critical), null, resolved, Now).ShouldBe(CandidateOutcome.Create);
    }

    [Fact]
    public void Dedup_key_ignores_direction_and_value()
    {
        var low = new AlertCandidate(Patient, AlertType.ThresholdBreach, AlertSeverity.Medium, VitalType.HeartRate, "HeartRate", 45, "low");
        var high = new AlertCandidate(Patient, AlertType.ThresholdBreach, AlertSeverity.Medium, VitalType.HeartRate, "HeartRate", 120, "high");
        low.DedupKey.ShouldBe(high.DedupKey);
    }

    [Theory]
    [InlineData(News2Band.High, AlertSeverity.Critical)]
    [InlineData(News2Band.Medium, AlertSeverity.High)]
    [InlineData(News2Band.LowMedium, AlertSeverity.Medium)]
    public void News2_band_maps_to_severity(News2Band band, AlertSeverity expected) =>
        AlertPolicy.ForNews2(band).ShouldBe(expected);
}
