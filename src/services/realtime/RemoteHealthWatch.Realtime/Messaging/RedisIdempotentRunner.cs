using StackExchange.Redis;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Realtime.Messaging;

/// <summary>Realtime has no database; a short-lived Redis marker is enough to avoid pushing a redelivered event twice.</summary>
public sealed class RedisIdempotentRunner(IConnectionMultiplexer redis) : IIdempotentRunner
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public async Task<bool> RunOnceAsync(Guid eventId, string consumer, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        var key = $"rhw:realtime:processed:{consumer}:{eventId:N}";
        var db = redis.GetDatabase();
        if (!await db.StringSetAsync(key, 1, Ttl, When.NotExists)) return false;
        try
        {
            await work(ct);
            return true;
        }
        catch
        {
            await db.KeyDeleteAsync(key);
            throw;
        }
    }
}
