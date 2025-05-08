using System.Diagnostics.Metrics;

namespace RemoteHealthWatch.Alerts.Services;

public static class AlertMetrics
{
    public const string MeterName = "RemoteHealthWatch.Alerts";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Raised = Meter.CreateCounter<long>("rhw.alerts.raised", "{alert}");
    public static readonly Counter<long> Merged = Meter.CreateCounter<long>("rhw.alerts.merged", "{alert}");
    public static readonly Counter<long> Suppressed = Meter.CreateCounter<long>("rhw.alerts.suppressed", "{alert}");
    public static readonly Counter<long> Escalations = Meter.CreateCounter<long>("rhw.alerts.escalations", "{escalation}");
    public static readonly Counter<long> Notifications = Meter.CreateCounter<long>("rhw.alerts.notifications", "{message}");
    public static readonly Histogram<double> TimeToAcknowledge = Meter.CreateHistogram<double>("rhw.alerts.time_to_ack", "s");
}
