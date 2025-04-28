using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.Vitals.Data;

/// <summary>Append-only write side. Clustered columnstore, partitioned by month on MeasuredAt.</summary>
public class VitalReading
{
    public Guid DeviceId { get; set; }
    public long Seq { get; set; }
    public Guid PatientId { get; set; }
    public VitalType Vital { get; set; }
    public double Value { get; set; }
    public double? Value2 { get; set; }
    public DateTime MeasuredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public bool Late { get; set; }
}

/// <summary>Rowstore dedup index. The columnstore can't enforce (DeviceId, Seq) uniqueness because its indexes must include the partition column.</summary>
public class ReadingKey
{
    public Guid DeviceId { get; set; }
    public long Seq { get; set; }
    public DateTime StoredAt { get; set; }
}

public class DeviceCursor
{
    public Guid DeviceId { get; set; }
    public Guid PatientId { get; set; }
    public long HighestSeq { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public abstract class Rollup
{
    public Guid PatientId { get; set; }
    public VitalType Vital { get; set; }
    public DateTime BucketStart { get; set; }
    public int Count { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double AvgValue { get; set; }
    public double? AvgValue2 { get; set; }
}

public class MinuteRollup : Rollup;

public class HourRollup : Rollup;

public class DeviceState
{
    public Guid DeviceId { get; set; }
    public Guid PatientId { get; set; }
    public string DeviceType { get; set; } = "";
    public bool Paired { get; set; }
    public int ExpectedIntervalSeconds { get; set; }
    public DateTime? SilentSince { get; set; }
    public DateTime UpdatedAt { get; set; }
}
