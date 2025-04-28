using System.Text.Json;
using StackExchange.Redis;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Vitals.Services;

public sealed record LatestValue(VitalType Vital, double Value, double? Value2, string Unit, DateTimeOffset MeasuredAt, Guid DeviceId);

/// <summary>Read model: the newest value per vital per patient, in a Redis hash.</summary>
public sealed class SnapshotStore(IConnectionMultiplexer redis)
{
    private static string Key(Guid patientId) => $"rhw:vitals:latest:{patientId}";

    public async Task<int> ApplyAsync(Guid patientId, Guid deviceId, IEnumerable<NormalizedReading> readings)
    {
        var newest = readings.GroupBy(r => r.Vital).Select(g => g.MaxBy(r => r.MeasuredAt)!).ToList();
        if (newest.Count == 0) return 0;

        var db = redis.GetDatabase();
        var key = Key(patientId);
        var current = await db.HashGetAsync(key, newest.Select(r => (RedisValue)r.Vital.ToString()).ToArray());
        var updates = new List<HashEntry>();
        for (var i = 0; i < newest.Count; i++)
        {
            var existing = current[i].HasValue ? JsonSerializer.Deserialize<LatestValue>(current[i].ToString(), Json) : null;
            // a late reading must never replace a newer value
            if (existing is not null && existing.MeasuredAt >= newest[i].MeasuredAt) continue;
            var r = newest[i];
            updates.Add(new HashEntry(r.Vital.ToString(),
                JsonSerializer.Serialize(new LatestValue(r.Vital, r.Value, r.Value2, r.Unit, r.MeasuredAt, deviceId), Json)));
        }
        if (updates.Count > 0)
        {
            await db.HashSetAsync(key, updates.ToArray());
            await db.KeyExpireAsync(key, TimeSpan.FromDays(30));
        }
        return updates.Count;
    }

    public async Task<IReadOnlyList<LatestValue>> GetAsync(Guid patientId)
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(Key(patientId));
        return entries.Select(e => JsonSerializer.Deserialize<LatestValue>(e.Value.ToString(), Json)!)
            .OrderBy(v => v.Vital).ToList();
    }

    public Task ClearAsync(Guid patientId) => redis.GetDatabase().KeyDeleteAsync(Key(patientId));

    private static readonly JsonSerializerOptions Json = EventEnvelope.Json;
}
