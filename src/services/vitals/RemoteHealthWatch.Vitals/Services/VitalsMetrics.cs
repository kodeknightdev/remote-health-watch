using System.Diagnostics.Metrics;

namespace RemoteHealthWatch.Vitals.Services;

public static class VitalsMetrics
{
    public const string MeterName = "RemoteHealthWatch.Vitals";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Stored = Meter.CreateCounter<long>("rhw.vitals.readings.stored", "{reading}");
    public static readonly Counter<long> Duplicates = Meter.CreateCounter<long>("rhw.vitals.readings.duplicate", "{reading}");
    public static readonly Counter<long> Late = Meter.CreateCounter<long>("rhw.vitals.readings.late", "{reading}");
    public static readonly Counter<long> Rejected = Meter.CreateCounter<long>("rhw.vitals.readings.rejected", "{reading}");

    // device clock to stored; includes device buffering, so it's the number clinicians actually feel
    public static readonly Histogram<double> IngestionLag = Meter.CreateHistogram<double>("rhw.vitals.ingestion.lag", "ms");
}
