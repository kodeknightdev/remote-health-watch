using Microsoft.EntityFrameworkCore;
using Quartz;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Alerts.Messaging;
using RemoteHealthWatch.Alerts.Notifications;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Caching;
using RemoteHealthWatch.ServiceDefaults.Data;
using RemoteHealthWatch.ServiceDefaults.Health;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.ServiceDefaults.Validation;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("alerts");
builder.Services.AddRemoteHealthWatchAuth(builder.Configuration);
var alertsDb = builder.Configuration.GetConnectionString("AlertsDb");
builder.Services.AddDbContext<AlertsDb>(o => o.UseSqlServer(alertsDb));
builder.Services.AddRemoteHealthWatchCaching(builder.Configuration, "alerts");
builder.Services.AddRemoteHealthWatchControllers<Program>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "AlertsDb");
builder.Services.AddPatientDirectoryClient(builder.Configuration);

builder.Services.AddRemoteHealthWatchMessaging(builder.Configuration, "alerts")
    .WithOutbox<AlertsDb>()
    .Consume<ReadingsHandler>("alerts-readings", Topics.ReadingsNormalized)
    .Consume<RiskHandler>("alerts-risk", Topics.RiskScoreUpdated, Topics.RiskAnomalyDetected, Topics.RiskDeteriorationTrend)
    .Consume<DeviceHandler>("alerts-devices", Topics.DeviceSilent, Topics.DevicePaired, Topics.DeviceUnpaired, Topics.DevicePairingFailed)
    .Consume<PatientHandler>("alerts-patients", Topics.ThresholdsChanged, Topics.CareTeamChanged, Topics.PatientDischarged);

builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<EscalationManager>();
builder.Services.AddScoped<CareTeamDirectory>();
builder.Services.AddScoped<IAlertNotifier, AlertNotifier>();
builder.Services.AddScoped<IEscalationScheduler, QuartzEscalationScheduler>();
builder.Services.AddHostedService<EscalationRecovery>();
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<ISmsSender, LoggingSmsSender>();
builder.Services.AddHttpClient<StaffDirectory>(c => c.BaseAddress = new Uri(builder.Configuration["Services:IdentityHttp"] ?? "http://localhost:5101/"))
    .AddHttpMessageHandler<ServiceTokenHandler>()
    .AddStandardResilienceHandler();

builder.Services.AddQuartz(q =>
{
    q.SchedulerName = "remote-health-watch-alerts";
    q.SchedulerId = "AUTO";
    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.RetryInterval = TimeSpan.FromSeconds(10);
        store.UseSqlServer(alertsDb!);
        store.UseSystemTextJsonSerializer();
        // several alerts replicas share one job store; each trigger fires on exactly one of them
        store.UseClustering(c =>
        {
            c.CheckinInterval = TimeSpan.FromSeconds(10);
            c.CheckinMisfireThreshold = TimeSpan.FromSeconds(30);
        });
    });
    q.AddJob<DailyDigestJob>(DailyDigestJob.Key, j => j.StoreDurably());
    q.AddTrigger(t => t.ForJob(DailyDigestJob.Key).WithIdentity("daily-digest-0700", "digests")
        .WithCronSchedule(builder.Configuration["Notifications:DigestCron"] ?? "0 0 7 * * ?"));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

var app = builder.Build();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Quartz needs its tables before the hosted scheduler starts, so migrate even outside Development
await app.Services.MigrateAsync<AlertsDb>(app.Environment, force: app.Configuration.GetValue("Database:MigrateOnStartup", true));
app.Run();

public partial class Program;
