using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;

namespace RemoteHealthWatch.Alerts.Services;

public sealed record StaffMember(Guid Id, string Email, string DisplayName, string Role, bool IsOnCall);

/// <summary>Identity's staff list, used for the on-call doctor and coordinator fallbacks and the daily digest.</summary>
public sealed class StaffDirectory(HttpClient http, HybridCache cache)
{
    public async Task<IReadOnlyList<StaffMember>> AllAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync("staff-directory", async token =>
            await http.GetFromJsonAsync<List<StaffMember>>("api/users/directory", token) ?? [],
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
            cancellationToken: ct);
}
