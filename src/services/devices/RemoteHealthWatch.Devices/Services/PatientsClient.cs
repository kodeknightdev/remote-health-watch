using System.Net;

namespace RemoteHealthWatch.Devices.Services;

public sealed record PairingEligibility(bool Eligible, string? Status, string? Reason);

/// <summary>REST call into patients to check a patient can take a device. Retries, timeout and breaker come from the resilience handler.</summary>
public sealed class PatientsClient(HttpClient http)
{
    public async Task<PairingEligibility> CheckEligibilityAsync(Guid patientId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"internal/patients/{patientId}/pairing-eligibility", ct);
        if (response.StatusCode == HttpStatusCode.Forbidden) return new PairingEligibility(false, null, "not-on-care-team");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PairingEligibility>(ct) ?? new PairingEligibility(false, null, "empty-response");
    }
}
