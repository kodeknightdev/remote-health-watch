using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteHealthWatch.Seed;

public sealed record StaffUser(Guid Id, string Email, string DisplayName, string Role);
public sealed record DeviceDto(Guid Id, string Serial, string Status, Guid? PatientId, string KeyPrefix);
public sealed record DeviceWithKey(DeviceDto Device, string ApiKey);
public sealed record PatientSummary(Guid Id, string Mrn, string Name, string Status);

public sealed class SeedClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task LoginAsync(string email, string password, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await http.PostAsJsonAsync("api/auth/login", new { email, password }, ct);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
                    return;
                }
                Console.WriteLine($"login attempt {attempt}: {(int)response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"login attempt {attempt}: {ex.Message}");
            }
            if (attempt >= 60) throw new InvalidOperationException("Gave up waiting for the gateway and identity service");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    public async Task<IReadOnlyList<StaffUser>> StaffAsync(string role, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<StaffUser>>($"api/users?role={role}", Json, ct) ?? [];

    public async Task<IReadOnlyList<PatientSummary>> PatientsAsync(CancellationToken ct)
    {
        var active = await http.GetFromJsonAsync<List<PatientSummary>>("api/patients", Json, ct) ?? [];
        return active;
    }

    public async Task<IReadOnlyList<DeviceDto>> DevicesAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<DeviceDto>>("api/devices", Json, ct) ?? [];

    public async Task<DeviceWithKey> RegisterDeviceAsync(string serial, string type, string firmware, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/devices", new { serial, type, firmware }, Json, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<DeviceWithKey>(Json, ct))!;
    }

    public async Task<DeviceWithKey> RotateKeyAsync(Guid deviceId, CancellationToken ct)
    {
        using var response = await http.PostAsync($"api/devices/{deviceId}/rotate-key", null, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<DeviceWithKey>(Json, ct))!;
    }

    public async Task<Guid> EnrollAsync(object request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/patients", request, Json, ct);
        await EnsureAsync(response, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {text}", null,
            response.StatusCode == 0 ? HttpStatusCode.InternalServerError : response.StatusCode);
    }
}
