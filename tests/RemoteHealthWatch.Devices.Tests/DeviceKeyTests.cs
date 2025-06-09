using RemoteHealthWatch.Devices.Domain;

namespace RemoteHealthWatch.Devices.Tests;

public class DeviceKeyTests
{
    private static readonly DateTimeOffset Now = new(2025, 4, 17, 10, 0, 0, TimeSpan.Zero);

    private static (Device Device, string Key) Registered()
    {
        var key = DeviceKeys.Generate();
        return (new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Paired, ApiKeyPrefix = key.Prefix, ApiKeyHash = key.Hash }, key.RawKey);
    }

    [Fact]
    public void Key_is_stored_only_as_a_hash()
    {
        var (device, key) = Registered();
        device.ApiKeyHash.ShouldNotContain(key);
        device.ApiKeyHash.Length.ShouldBe(64);
        DeviceKeys.PrefixOf(key).ShouldBe(device.ApiKeyPrefix);
    }

    [Fact]
    public void Valid_key_verifies_and_a_tampered_one_does_not()
    {
        var (device, key) = Registered();
        DeviceKeys.Verify(device, key, Now).ShouldBe(KeyCheck.Valid);
        DeviceKeys.Verify(device, key[..^1] + (key[^1] == 'a' ? 'b' : 'a'), Now).ShouldBe(KeyCheck.Invalid);
    }

    [Fact]
    public void Rotation_keeps_the_old_key_for_a_grace_period_only()
    {
        var (device, oldKey) = Registered();
        var newKey = DeviceKeys.Rotate(device, Now).RawKey;

        DeviceKeys.Verify(device, newKey, Now).ShouldBe(KeyCheck.Valid);
        DeviceKeys.Verify(device, oldKey, Now.AddMinutes(5)).ShouldBe(KeyCheck.ValidPrevious);
        DeviceKeys.Verify(device, oldKey, Now + DeviceKeys.RotationGrace + TimeSpan.FromSeconds(1)).ShouldBe(KeyCheck.Invalid);
    }

    [Fact]
    public void Revoked_device_is_rejected_with_any_key()
    {
        var (device, key) = Registered();
        var rotated = DeviceKeys.Rotate(device, Now).RawKey;
        DeviceKeys.Revoke(device);
        DeviceKeys.Verify(device, rotated, Now).ShouldBe(KeyCheck.Revoked);
        DeviceKeys.Verify(device, key, Now).ShouldBe(KeyCheck.Revoked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("rhwd_short_secret")]
    public void Malformed_keys_have_no_prefix(string? raw) => DeviceKeys.PrefixOf(raw).ShouldBeNull();
}
