using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Realtime.Hubs;
using RemoteHealthWatch.Realtime.Messaging;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Caching;
using RemoteHealthWatch.ServiceDefaults.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("realtime");
builder.Services.AddRemoteHealthWatchAuth(builder.Configuration, hubPath: LiveHub.Path);
builder.Services.AddRemoteHealthWatchCaching(builder.Configuration, "realtime");
builder.Services.AddPatientDirectoryClient(builder.Configuration);

var signalr = builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
var redis = builder.Configuration.GetConnectionString("Redis");
// the backplane lets any replica's consumer reach clients connected to any other replica
if (!string.IsNullOrWhiteSpace(redis))
    signalr.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("rhw-signalr"));

builder.Services.AddScoped<RemoteHealthWatch.ServiceDefaults.Messaging.IIdempotentRunner, RedisIdempotentRunner>();
builder.Services.AddRemoteHealthWatchMessaging(builder.Configuration, "realtime")
    .Consume<LiveEventsHandler>("realtime",
        Topics.ReadingsNormalized, Topics.RiskScoreUpdated, Topics.RiskAnomalyDetected, Topics.RiskDeteriorationTrend,
        Topics.AlertRaised, Topics.AlertAcknowledged, Topics.AlertEscalated, Topics.AlertResolved, Topics.CareTeamChanged);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.MapServiceDefaults();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<LiveHub>(LiveHub.Path);
app.Run();

public partial class Program;
