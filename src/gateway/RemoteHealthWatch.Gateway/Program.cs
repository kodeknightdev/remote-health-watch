using Microsoft.Extensions.Http.Resilience;
using RemoteHealthWatch.Gateway.Composition;
using RemoteHealthWatch.Gateway.RateLimiting;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Validation;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("gateway");
builder.Services.AddRemoteHealthWatchAuth(builder.Configuration, hubPath: "/hubs");
builder.Services.AddGatewayRateLimits(builder.Configuration);
builder.Services.AddRemoteHealthWatchControllers<Program>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// typed clients for composition, one per downstream cluster, reusing the proxy's cluster addresses
foreach (var cluster in new[] { "patients", "vitals", "alerts", "risk", "devices" })
{
    var address = builder.Configuration[$"ReverseProxy:Clusters:{cluster}:Destinations:d1:Address"]
        ?? throw new InvalidOperationException($"No address for cluster {cluster}");
    builder.Services.AddHttpClient(cluster, c => c.BaseAddress = new Uri(address))
        .AddStandardResilienceHandler(o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
            o.Retry.MaxRetryAttempts = 2;
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        });
}
builder.Services.AddScoped<DownstreamClient>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders("X-Composition-Degraded", "Retry-After")));

var app = builder.Build();
app.MapServiceDefaults();
app.UseCors();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting(GatewayRateLimits.PerUser);
app.MapReverseProxy();
app.Run();

public partial class Program;
