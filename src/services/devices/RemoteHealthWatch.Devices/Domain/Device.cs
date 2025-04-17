namespace RemoteHealthWatch.Devices.Domain;

public enum DeviceType
{
    MultiParameterPatch,
    BloodPressureCuff,
    PulseOximeter,
    Glucometer,
    Thermometer
}

public enum DeviceStatus
{
    Available,
    Paired,
    Revoked
}

public class Device
{
    public Guid Id { get; set; }
    public string Serial { get; set; } = "";
    public DeviceType Type { get; set; }
    public string Firmware { get; set; } = "";
    public DeviceStatus Status { get; set; }
    public Guid? PatientId { get; set; }
    public DateTimeOffset? PairedAt { get; set; }
    public int ExpectedIntervalSeconds { get; set; } = 300;

    public string ApiKeyPrefix { get; set; } = "";
    public string ApiKeyHash { get; set; } = "";
    public DateTimeOffset KeyIssuedAt { get; set; }

    /// <summary>The key before the last rotation stays valid for a short grace period so in-flight devices can pick up the new one.</summary>
    public string? PreviousKeyHash { get; set; }
    public DateTimeOffset? PreviousKeyValidUntil { get; set; }

    public int? BatteryPercent { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public long? LastSeq { get; set; }
    public DateTimeOffset? SilentSince { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public Guid Version { get; set; }
}
