using Microsoft.Extensions.Caching.Hybrid;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Contracts.Grpc;
using RemoteHealthWatch.ServiceDefaults.Access;

namespace RemoteHealthWatch.Alerts.Services;

public sealed record StaffContact(Guid UserId, string Role, string DisplayName, string Email, string Phone, bool IsPrimary);

/// <summary>Cache-aside over the patients gRPC directory. Entries are dropped when patients publishes a change.</summary>
public sealed class CareTeamDirectory(PatientDirectory.PatientDirectoryClient client, HybridCache cache)
{
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task<IReadOnlyDictionary<string, ThresholdLimits>> ThresholdsAsync(Guid patientId, CancellationToken ct) =>
        await cache.GetOrCreateAsync(CacheKeys.Thresholds(patientId), async token =>
        {
            var set = await client.GetThresholdsAsync(new PatientRequest { PatientId = patientId.ToString() }, cancellationToken: token);
            return set.Thresholds.ToDictionary(t => t.Metric, t => new ThresholdLimits(t.Metric,
                t.HasLowCritical ? t.LowCritical : null, t.HasLowWarning ? t.LowWarning : null,
                t.HasHighWarning ? t.HighWarning : null, t.HasHighCritical ? t.HighCritical : null));
        }, Options, cancellationToken: ct);

    public async Task<IReadOnlyList<StaffContact>> CareTeamAsync(Guid patientId, CancellationToken ct) =>
        await cache.GetOrCreateAsync(CacheKeys.CareTeam(patientId), async token =>
        {
            var team = await client.GetCareTeamAsync(new PatientRequest { PatientId = patientId.ToString() }, cancellationToken: token);
            return team.Members.Select(m => new StaffContact(Guid.Parse(m.UserId), m.Role, m.DisplayName, m.Email, m.Phone, m.IsPrimary))
                .ToList();
        }, Options, cancellationToken: ct);

    public async Task InvalidateAsync(Guid patientId, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKeys.Thresholds(patientId), ct);
        await cache.RemoveAsync(CacheKeys.CareTeam(patientId), ct);
    }
}
