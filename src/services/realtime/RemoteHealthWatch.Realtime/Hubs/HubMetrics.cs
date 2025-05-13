using System.Diagnostics.Metrics;

namespace RemoteHealthWatch.Realtime.Hubs;

public static class HubMetrics
{
    public const string MeterName = "RemoteHealthWatch.Realtime";
    private static readonly Meter Meter = new(MeterName);

    public static readonly UpDownCounter<long> Connections = Meter.CreateUpDownCounter<long>("rhw.realtime.connections", "{connection}");
    public static readonly Counter<long> Pushed = Meter.CreateCounter<long>("rhw.realtime.pushed", "{message}");

    // event creation to hub send; the browser adds its own network hop on top
    public static readonly Histogram<double> PushLag = Meter.CreateHistogram<double>("rhw.realtime.push.lag", "ms");
}
