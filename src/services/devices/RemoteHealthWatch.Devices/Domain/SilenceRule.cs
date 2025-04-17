namespace RemoteHealthWatch.Devices.Domain;

public static class SilenceRule
{
    public const int MissedIntervals = 3;
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(60);

    /// <summary>A paired device is silent once it has missed three expected intervals (never less than a minute).</summary>
    public static TimeSpan Allowance(int expectedIntervalSeconds) =>
        TimeSpan.FromSeconds(Math.Max(Minimum.TotalSeconds, expectedIntervalSeconds * MissedIntervals));

    public static bool IsSilent(Device device, DateTimeOffset now)
    {
        if (device.Status != DeviceStatus.Paired || device.SilentSince is not null) return false;
        var reference = device.LastSeenAt ?? device.PairedAt;
        return reference is not null && now - reference.Value > Allowance(device.ExpectedIntervalSeconds);
    }
}
