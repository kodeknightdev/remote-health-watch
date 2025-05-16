using System.Net;
using System.Text.Json;

namespace RemoteHealthWatch.Gateway.Composition;

/// <summary>Result of one leg of a composed request; failures are reported, not thrown, so the rest still renders.</summary>
public sealed record Leg(string Name, JsonElement? Data, int Status, string? Error)
{
    public bool Ok => Status is >= 200 and < 300;
}

public sealed class DownstreamClient(IHttpClientFactory factory, IHttpContextAccessor accessor)
{
    public async Task<Leg> GetAsync(string cluster, string name, string path, CancellationToken ct)
    {
        var client = factory.CreateClient(cluster);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var auth = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth)) request.Headers.TryAddWithoutValidation("Authorization", auth);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return new Leg(name, null, 404, null);
            if (!response.IsSuccessStatusCode) return new Leg(name, null, (int)response.StatusCode, response.ReasonPhrase);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return new Leg(name, doc.RootElement.Clone(), (int)response.StatusCode, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.CircuitBreaker.BrokenCircuitException
                                       or Polly.Timeout.TimeoutRejectedException)
        {
            return new Leg(name, null, StatusCodes.Status503ServiceUnavailable, ex.GetType().Name);
        }
    }
}
