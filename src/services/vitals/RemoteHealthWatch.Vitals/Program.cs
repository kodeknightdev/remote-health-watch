using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Caching;
using RemoteHealthWatch.ServiceDefaults.Data;
using RemoteHealthWatch.ServiceDefaults.Health;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.ServiceDefaults.Validation;
using RemoteHealthWatch.Vitals.Data;
using RemoteHealthWatch.Vitals.Messaging;
using RemoteHealthWatch.Vitals.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("vitals");
builder.Services.AddRemoteHealthWatchAuth(builder.Configuration);
builder.Services.AddDbContext<VitalsDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("VitalsDb"),
    sql => sql.CommandTimeout(30)));
builder.Services.AddRemoteHealthWatchCaching(builder.Configuration, "vitals");
builder.Services.AddRemoteHealthWatchControllers<Program>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "VitalsDb");
builder.Services.AddPatientDirectoryClient(builder.Configuration);

builder.Services.AddScoped<RollupWriter>();
builder.Services.AddScoped<ReadingProcessor>();
builder.Services.AddSingleton<SnapshotStore>();

// every vitals replica joins the same group, so the readings partitions are split between them
builder.Services.AddRemoteHealthWatchMessaging(builder.Configuration, "vitals")
    .WithOutbox<VitalsDb>()
    .Consume<RawReadingsHandler>("vitals", Topics.ReadingsRaw)
    .Consume<DeviceEventsHandler>("vitals-devices", Topics.DevicePaired, Topics.DeviceUnpaired, Topics.DeviceSilent);

var app = builder.Build();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.Services.MigrateAsync<VitalsDb>(app.Environment);
app.Run();

public partial class Program;
