using System.Security.Cryptography;
using System.Text;

namespace RemoteHealthWatch.Devices.Domain;

public sealed record GeneratedKey(string RawKey, string Prefix, string Hash);

public enum KeyCheck
{
    Valid,
    ValidPrevious,
    Invalid,
    Revoked
}

/// <summary>
/// Keys look like rhwd_{prefix}_{secret}. The prefix is stored in clear for lookup, the whole key only as a SHA-256 hash.
/// Plain SHA-256 is fine here because the secret is 32 random bytes, not a user password.
/// </summary>
public static class DeviceKeys
{
    public const string HeaderName = "X-Device-Key";
    public static readonly TimeSpan RotationGrace = TimeSpan.FromMinutes(10);

    public static GeneratedKey Generate()
    {
        var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var raw = $"rhwd_{prefix}_{secret}";
        return new GeneratedKey(raw, prefix, Hash(raw));
    }

    public static string? PrefixOf(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith("rhwd_", StringComparison.Ordinal)) return null;
        var parts = raw.Split('_', 3);
        return parts.Length == 3 && parts[1].Length == 8 ? parts[1] : null;
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static KeyCheck Verify(Device device, string raw, DateTimeOffset now)
    {
        if (device.Status == DeviceStatus.Revoked) return KeyCheck.Revoked;
        var hash = Hash(raw);
        if (FixedEquals(hash, device.ApiKeyHash)) return KeyCheck.Valid;
        if (device.PreviousKeyHash is not null && device.PreviousKeyValidUntil > now && FixedEquals(hash, device.PreviousKeyHash))
            return KeyCheck.ValidPrevious;
        return KeyCheck.Invalid;
    }

    public static GeneratedKey Rotate(Device device, DateTimeOffset now)
    {
        var key = Generate();
        device.PreviousKeyHash = device.ApiKeyHash;
        device.PreviousKeyValidUntil = now + RotationGrace;
        device.ApiKeyPrefix = key.Prefix;
        device.ApiKeyHash = key.Hash;
        device.KeyIssuedAt = now;
        return key;
    }

    public static void Revoke(Device device)
    {
        device.Status = DeviceStatus.Revoked;
        device.PreviousKeyHash = null;
        device.PreviousKeyValidUntil = null;
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
