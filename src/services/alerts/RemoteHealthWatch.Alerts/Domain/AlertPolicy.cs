using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Domain;

public sealed record AlertCandidate(Guid PatientId, AlertType Type, AlertSeverity Severity, VitalType? Vital, string? Metric, double? Value, string Message)
{
    // direction stays out of the key on purpose: a patient bouncing between low and high is one problem
    public string DedupKey => $"{Type}:{PatientId:N}:{Metric ?? Vital?.ToString() ?? "-"}";
}

public enum CandidateOutcome
{
    Create,
    MergeIntoOpen,
    UpgradeOpen,
    Suppressed
}

/// <summary>Dedup and suppression rules that keep a noisy sensor from producing an alert storm.</summary>
public static class AlertPolicy
{
    /// <summary>After an alert is resolved, the same problem at the same or lower severity stays quiet for this long.</summary>
    public static TimeSpan SuppressionWindow(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => TimeSpan.FromMinutes(5),
        AlertSeverity.High => TimeSpan.FromMinutes(10),
        AlertSeverity.Medium => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(60)
    };

    public static CandidateOutcome Decide(AlertCandidate candidate, Alert? open, Alert? lastResolved, DateTimeOffset now)
    {
        if (open is not null)
            return candidate.Severity > open.Severity ? CandidateOutcome.UpgradeOpen : CandidateOutcome.MergeIntoOpen;

        if (lastResolved?.ResolvedAt is { } resolvedAt
            && candidate.Severity <= lastResolved.Severity
            && now - resolvedAt < SuppressionWindow(lastResolved.Severity))
            return CandidateOutcome.Suppressed;

        return CandidateOutcome.Create;
    }

    public static AlertSeverity ForNews2(News2Band band) => band switch
    {
        News2Band.High => AlertSeverity.Critical,
        News2Band.Medium => AlertSeverity.High,
        News2Band.LowMedium => AlertSeverity.Medium,
        _ => AlertSeverity.Low
    };
}
