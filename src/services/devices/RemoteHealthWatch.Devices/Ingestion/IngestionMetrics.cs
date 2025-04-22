using System.Diagnostics.Metrics;

namespace RemoteHealthWatch.Devices.Ingestion;

public static class IngestionMetrics
{
    public const string MeterName = "RemoteHealthWatch.Ingestion";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Readings = Meter.CreateCounter<long>("rhw.ingestion.readings", "{reading}");
    public static readonly Counter<long> Batches = Meter.CreateCounter<long>("rhw.ingestion.batches", "{batch}");
    public static readonly Counter<long> Rejected = Meter.CreateCounter<long>("rhw.ingestion.rejected", "{batch}");
    public static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("rhw.ingestion.publish.duration", "ms");
}
