using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Alerts.Notifications;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.TestSupport;

namespace RemoteHealthWatch.Alerts.Tests;

public class EscalationManagerTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2025, 6, 9, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeScheduler _scheduler = new();
    private readonly FakeNotifier _notifier = new();

    private AlertsDb NewDb(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AlertsDb>()
        .UseInMemoryDatabase(_dbName)
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .AddInterceptors(interceptors)
        .Options);

    private EscalationManager Manager(AlertsDb db, RecordingOutbox? outbox = null) =>
        new(db, outbox ?? new RecordingOutbox(), _scheduler, _notifier, new PostCommitActions(), _clock, NullLogger<EscalationManager>.Instance);

    private async Task<Guid> SeedRaisedCriticalAsync()
    {
        await using var db = NewDb();
        var alert = new Alert
        {
            Id = Guid.NewGuid(), PatientId = Guid.NewGuid(), Type = AlertType.ThresholdBreach, Severity = AlertSeverity.Critical,
            Status = AlertStatus.Raised, RaisedAt = _clock.GetUtcNow(), LastOccurredAt = _clock.GetUtcNow(), DedupKey = "k", Version = Guid.NewGuid()
        };
        var decision = EscalationStateMachine.Start(alert, _clock.GetUtcNow());
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        await Manager(db).ExecuteAsync(alert, decision, default);
        return alert.Id;
    }

    [Fact]
    public async Task Timeout_pages_the_doctor_and_schedules_the_next_level()
    {
        var id = await SeedRaisedCriticalAsync();
        _clock.Advance(TimeSpan.FromMinutes(2));

        var outbox = new RecordingOutbox();
        await using var db = NewDb();
        (await Manager(db, outbox).TimeoutAsync(id, EscalationStateMachine.Nurse, default)).ShouldBeTrue();

        _notifier.Pages.Select(p => p.Role).ShouldBe([Roles.Nurse, Roles.Doctor]);
        _scheduler.Scheduled.Last().ShouldBe((id, EscalationStateMachine.Doctor, _clock.GetUtcNow().AddMinutes(5)));
        outbox.Of<AlertEscalated>().Single().To.ShouldBe(AlertStatus.EscalatedDoctor);
    }

    [Fact]
    public async Task Restart_mid_escalation_neither_loses_nor_repeats_the_step()
    {
        var id = await SeedRaisedCriticalAsync();
        _clock.Advance(TimeSpan.FromMinutes(2));
        await using (var before = NewDb())
            await Manager(before).TimeoutAsync(id, EscalationStateMachine.Nurse, default);

        // fresh context and manager, as after a pod restart; Quartz refires the misfired nurse trigger
        _clock.Advance(TimeSpan.FromSeconds(40));
        await using (var after = NewDb())
            (await Manager(after).TimeoutAsync(id, EscalationStateMachine.Nurse, default)).ShouldBeFalse();

        _notifier.Pages.Count(p => p.Role == Roles.Doctor).ShouldBe(1);

        // and the doctor-level timer that was persisted before the restart still fires
        _clock.Advance(TimeSpan.FromMinutes(5));
        await using var later = NewDb();
        (await Manager(later).TimeoutAsync(id, EscalationStateMachine.Doctor, default)).ShouldBeTrue();
        (await later.Alerts.SingleAsync(a => a.Id == id)).Status.ShouldBe(AlertStatus.EscalatedCoordinator);
    }

    [Fact]
    public async Task Acknowledgement_racing_a_timeout_wins_without_a_page()
    {
        var id = await SeedRaisedCriticalAsync();
        _clock.Advance(TimeSpan.FromMinutes(2));
        var nurse = Guid.NewGuid();

        // the timeout job loads the alert, then the nurse's ack commits before the job saves
        var race = new BeforeSaveInterceptor(async () =>
        {
            await using var other = NewDb();
            (await Manager(other).AcknowledgeAsync(id, nurse, default)).ShouldBeTrue();
        });
        await using var db = NewDb(race);
        (await Manager(db).TimeoutAsync(id, EscalationStateMachine.Nurse, default)).ShouldBeFalse();

        await using var check = NewDb();
        var alert = await check.Alerts.SingleAsync(a => a.Id == id);
        alert.Status.ShouldBe(AlertStatus.Acknowledged);
        alert.AcknowledgedBy.ShouldBe(nurse);
        _notifier.Pages.ShouldNotContain(p => p.Role == Roles.Doctor);
        _scheduler.Cancelled.ShouldContain(id);
    }

    [Fact]
    public async Task Recovery_sweep_applies_an_overdue_timeout_once()
    {
        var id = await SeedRaisedCriticalAsync();
        _scheduler.Scheduled.Clear();
        _clock.Advance(TimeSpan.FromMinutes(4));

        var provider = new ServiceCollection()
            .AddScoped(_ => NewDb())
            .AddScoped(sp => Manager(sp.GetRequiredService<AlertsDb>()))
            .BuildServiceProvider();
        var sweeper = new EscalationRecovery(provider.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<EscalationRecovery>.Instance);

        (await sweeper.SweepAsync(default)).ShouldBe(1);
        (await sweeper.SweepAsync(default)).ShouldBe(0);
        _notifier.Pages.Count(p => p.Role == Roles.Doctor).ShouldBe(1);
    }

    private sealed class FakeScheduler : IEscalationScheduler
    {
        public List<(Guid AlertId, int Level, DateTimeOffset DueAt)> Scheduled { get; } = [];
        public List<Guid> Cancelled { get; } = [];

        public Task ScheduleAsync(Guid alertId, int level, DateTimeOffset dueAt, CancellationToken ct)
        {
            Scheduled.Add((alertId, level, dueAt));
            return Task.CompletedTask;
        }

        public Task CancelAsync(Guid alertId, CancellationToken ct)
        {
            Cancelled.Add(alertId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : IAlertNotifier
    {
        public List<(Guid AlertId, string Role, bool Final)> Pages { get; } = [];

        public Task NotifyAsync(Alert alert, string role, bool final, CancellationToken ct)
        {
            Pages.Add((alert.Id, role, final));
            return Task.CompletedTask;
        }
    }

    private sealed class BeforeSaveInterceptor(Func<Task> action) : SaveChangesInterceptor
    {
        private bool _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_fired)
            {
                _fired = true;
                await action();
            }
            return result;
        }
    }
}
