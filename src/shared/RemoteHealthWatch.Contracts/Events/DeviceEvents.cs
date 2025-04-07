namespace RemoteHealthWatch.Contracts.Events;

public sealed record RawReading(long Seq, VitalType Vital, double Value, double? Value2, string Unit, DateTimeOffset MeasuredAt);

public sealed record ReadingsBatchReceived(
    Guid BatchId,
    Guid DeviceId,
    Guid PatientId,
    DateTimeOffset ReceivedAt,
    int? BatteryPercent,
    IReadOnlyList<RawReading> Readings) : IIntegrationEvent
{
    public static string EventType => "devices.readings-batch-received";
    public static int SchemaVersion => 1;
}

public sealed record DevicePaired(Guid DeviceId, Guid PatientId, string DeviceType, int ExpectedIntervalSeconds, DateTimeOffset PairedAt)
    : IIntegrationEvent
{
    public static string EventType => "devices.device-paired";
    public static int SchemaVersion => 1;
}

public sealed record DeviceUnpaired(Guid DeviceId, Guid PatientId, string Reason, DateTimeOffset UnpairedAt) : IIntegrationEvent
{
    public static string EventType => "devices.device-unpaired";
    public static int SchemaVersion => 1;
}

public sealed record DevicePairingFailed(Guid PatientId, string Serial, string Reason, DateTimeOffset FailedAt) : IIntegrationEvent
{
    public static string EventType => "devices.device-pairing-failed";
    public static int SchemaVersion => 1;
}

public sealed record DeviceSilent(Guid DeviceId, Guid PatientId, DateTimeOffset LastSeenAt, int ExpectedIntervalSeconds, DateTimeOffset DetectedAt)
    : IIntegrationEvent
{
    public static string EventType => "devices.device-silent";
    public static int SchemaVersion => 1;
}
