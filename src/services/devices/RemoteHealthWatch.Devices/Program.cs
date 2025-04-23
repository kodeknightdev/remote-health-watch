using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.Devices.Ingestion;
using RemoteHealthWatch.Devices.Messaging;
using RemoteHealthWatch.Devices.Services;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Caching;
using RemoteHealthWatch.ServiceDefaults.Data;
using RemoteHealthWatch.ServiceDefaults.Health;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.ServiceDefaults.Validation;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("devices");
builder.Services.AddRemoteHealthWatchAuth(builder.Configuration);
builder.Services.AddDbContext<DevicesDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("DevicesDb")));
builder.Services.AddRemoteHealthWatchCaching(builder.Configuration, "devices");
builder.Services.AddRemoteHealthWatchControllers<Program>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "DevicesDb");

builder.Services.AddRemoteHealthWatchMessaging(builder.Configuration, "devices")
    .WithOutbox<DevicesDb>()
    .Consume<PatientEventsHandler>("devices", Topics.PatientEnrolled, Topics.PatientDischarged);

builder.Services.AddPatientDirectoryClient(builder.Configuration);
builder.Services.AddHttpClient<PatientsClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:PatientsHttp"] ?? "http://localhost:5102/"))
    .AddHttpMessageHandler<BearerForwardingHandler>()
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<DeviceAuthCache>();
builder.Services.AddScoped<DeviceService>();
builder.Services.AddHostedService<SilentDeviceDetector>();

var deviceLimit = builder.Configuration.GetSection("Ingestion:RateLimit");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // token bucket per device key prefix; a device resending its backlog after reconnect gets a burst, not a flood
    o.AddPolicy(RateLimits.PerDevice, ctx => RateLimitPartition.GetTokenBucketLimiter(
        DeviceKeys.PrefixOf(ctx.Request.Headers[DeviceKeys.HeaderName]) ?? "anonymous",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = deviceLimit.GetValue("TokenLimit", 20),
            TokensPerPeriod = deviceLimit.GetValue("TokensPerPeriod", 5),
            ReplenishmentPeriod = TimeSpan.FromSeconds(deviceLimit.GetValue("PeriodSeconds", 5)),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

await app.Services.MigrateAsync<DevicesDb>(app.Environment);
app.Run();

public partial class Program;
