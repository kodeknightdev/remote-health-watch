namespace RemoteHealthWatch.Contracts.Events;

/// <summary>A reading after unit normalisation. Late is set when it arrived behind a newer sequence from the same device.</summary>
public sealed record NormalizedReading(long Seq, VitalType Vital, double Value, double? Value2, string Unit, DateTimeOffset MeasuredAt, bool Late);

public sealed record ReadingsNormalized(Guid PatientId, Guid DeviceId, IReadOnlyList<NormalizedReading> Readings, DateTimeOffset StoredAt)
    : IIntegrationEvent
{
    public static string EventType => "vitals.readings-normalized";
    public static int SchemaVersion => 1;
}
