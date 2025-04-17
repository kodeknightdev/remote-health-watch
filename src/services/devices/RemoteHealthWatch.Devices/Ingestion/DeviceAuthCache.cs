using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;

namespace RemoteHealthWatch.Devices.Ingestion;

/// <summary>Cache-aside for the per-request key lookup. Short local TTL because other replicas only learn about rotation via expiry.</summary>
public sealed class DeviceAuthCache(HybridCache cache, IServiceScopeFactory scopes)
{
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };

    public async Task<Device?> GetAsync(string prefix, CancellationToken ct)
    {
        var device = await cache.GetOrCreateAsync($"device-key:{prefix}", async token =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevicesDb>();
            return await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.ApiKeyPrefix == prefix, token);
        }, Options, cancellationToken: ct);
        return device;
    }

    public ValueTask InvalidateAsync(string prefix, CancellationToken ct) => cache.RemoveAsync($"device-key:{prefix}", ct);
}
