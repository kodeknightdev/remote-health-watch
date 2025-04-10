using System.Diagnostics.Metrics;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public static class MessagingMetrics
{
    public const string MeterName = "RemoteHealthWatch.Messaging";
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> ConsumedCount = Meter.CreateCounter<long>("rhw.messaging.consumed", "{message}");
    private static readonly Counter<long> FailedCount = Meter.CreateCounter<long>("rhw.messaging.failed", "{message}");
    private static readonly Histogram<double> HandlerDuration = Meter.CreateHistogram<double>("rhw.messaging.handler.duration", "ms");

    // end-to-end lag from when the event was created to when this consumer finished with it
    private static readonly Histogram<double> EventLag = Meter.CreateHistogram<double>("rhw.messaging.event.lag", "ms");

    public static void Consumed(string topic, string group, TimeSpan duration, DateTimeOffset occurredAt, TimeProvider clock)
    {
        var tags = new KeyValuePair<string, object?>[] { new("topic", topic), new("group", group) };
        ConsumedCount.Add(1, tags);
        HandlerDuration.Record(duration.TotalMilliseconds, tags);
        EventLag.Record((clock.GetUtcNow() - occurredAt).TotalMilliseconds, tags);
    }

    public static void Failed(string topic, string group) =>
        FailedCount.Add(1, new KeyValuePair<string, object?>("topic", topic), new KeyValuePair<string, object?>("group", group));
}
