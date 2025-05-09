using Quartz;

namespace RemoteHealthWatch.Alerts.Escalation;

public interface IEscalationScheduler
{
    Task ScheduleAsync(Guid alertId, int level, DateTimeOffset dueAt, CancellationToken ct);
    Task CancelAsync(Guid alertId, CancellationToken ct);
}

/// <summary>Timeouts as Quartz triggers in the SQL Server job store, so they survive restarts and run once per cluster.</summary>
public sealed class QuartzEscalationScheduler(ISchedulerFactory factory) : IEscalationScheduler
{
    public const string Group = "escalations";

    public async Task ScheduleAsync(Guid alertId, int level, DateTimeOffset dueAt, CancellationToken ct)
    {
        var scheduler = await factory.GetScheduler(ct);
        var job = JobBuilder.Create<EscalationTimeoutJob>()
            .WithIdentity(JobKeyFor(alertId, level))
            .UsingJobData(EscalationTimeoutJob.AlertIdKey, alertId.ToString())
            .UsingJobData(EscalationTimeoutJob.LevelKey, level.ToString())
            .RequestRecovery()
            .Build();
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"escalate-{alertId:N}-{level}", Group)
            .StartAt(dueAt)
            // fired late after downtime is still correct: the job re-checks the level before acting
            .WithSimpleSchedule(s => s.WithMisfireHandlingInstructionFireNow())
            .Build();
        await scheduler.ScheduleJob(job, [trigger], replace: true, ct);
    }

    public async Task CancelAsync(Guid alertId, CancellationToken ct)
    {
        var scheduler = await factory.GetScheduler(ct);
        await scheduler.DeleteJobs(Enumerable.Range(0, EscalationStateMachine.Unanswered).Select(l => JobKeyFor(alertId, l)).ToList(), ct);
    }

    private static JobKey JobKeyFor(Guid alertId, int level) => new($"escalate-{alertId:N}-{level}", Group);
}

public sealed class EscalationTimeoutJob(EscalationManager manager, ILogger<EscalationTimeoutJob> logger) : IJob
{
    public const string AlertIdKey = "alertId";
    public const string LevelKey = "level";

    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.MergedJobDataMap;
        var alertId = Guid.Parse(data.GetString(AlertIdKey)!);
        var level = int.Parse(data.GetString(LevelKey)!);
        var applied = await manager.TimeoutAsync(alertId, level, context.CancellationToken);
        if (!applied) logger.LogDebug("Timeout for alert {AlertId} level {Level} was stale", alertId, level);
    }
}
