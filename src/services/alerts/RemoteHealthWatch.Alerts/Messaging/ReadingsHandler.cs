using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Messaging;

/// <summary>Threshold rules on every normalized reading, against the patient's personal limits.</summary>
public sealed class ReadingsHandler(CareTeamDirectory directory, AlertService alerts, TimeProvider clock) : IEventHandler
{
    // a reading that turns up this late (device buffered it offline) is history, not something to page about
    private static readonly TimeSpan MaxReadingAge = TimeSpan.FromMinutes(15);

    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var batch = envelope.DataAs<ReadingsNormalized>();
        var now = clock.GetUtcNow();
        var fresh = batch.Readings.Where(r => now - r.MeasuredAt <= MaxReadingAge).ToList();
        if (fresh.Count == 0) return;

        var limits = await directory.ThresholdsAsync(batch.PatientId, ct);
        var worst = fresh
            .SelectMany(r => ThresholdEvaluator.Evaluate(r, limits))
            .GroupBy(b => b.Metric)
            .Select(g => g.OrderByDescending(b => b.Critical).ThenByDescending(b => Math.Abs(b.Value - b.Limit)).First());

        foreach (var breach in worst)
        {
            await alerts.RaiseAsync(new AlertCandidate(batch.PatientId, AlertType.ThresholdBreach, breach.Severity, breach.Vital,
                breach.Metric, breach.Value, ThresholdEvaluator.Describe(breach)), ct);
        }
    }
}
