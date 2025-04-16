using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Grpc;
using RemoteHealthWatch.Patients.Messaging;
using RemoteHealthWatch.Patients.Services;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Data;
using RemoteHealthWatch.ServiceDefaults.Health;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.ServiceDefaults.Validation;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("patients");

// REST on one port, h2c gRPC on another, since plain-http gRPC can't share an HTTP/1.1 listener
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(builder.Configuration.GetValue("Ports:Http", 8080), o => o.Protocols = HttpProtocols.Http1AndHttp2);
    k.ListenAnyIP(builder.Configuration.GetValue("Ports:Grpc", 8081), o => o.Protocols = HttpProtocols.Http2);
});

builder.Services.AddRemoteHealthWatchAuth(builder.Configuration);
builder.Services.AddDbContext<PatientsDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("PatientsDb")));
builder.Services.AddScoped<PatientService>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddHostedService<BaselineWatcher>();
builder.Services.AddGrpc();
builder.Services.AddRemoteHealthWatchControllers<Program>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "PatientsDb");

builder.Services.AddRemoteHealthWatchMessaging(builder.Configuration, "patients")
    .WithOutbox<PatientsDb>()
    .Consume<DeviceEventsHandler>("patients", Topics.DevicePaired, Topics.DeviceUnpaired, Topics.DevicePairingFailed);

var app = builder.Build();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<PatientDirectoryService>();

await app.Services.MigrateAsync<PatientsDb>(app.Environment);
app.Run();

public partial class Program;
