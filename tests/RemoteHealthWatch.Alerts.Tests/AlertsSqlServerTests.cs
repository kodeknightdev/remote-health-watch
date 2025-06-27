using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Alerts.Notifications;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.TestSupport;
using Testcontainers.MsSql;

namespace RemoteHealthWatch.Alerts.Tests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    public MsSqlContainer? Container { get; private set; }

    public async Task InitializeAsync()
    {
        if (new DockerFactAttribute().Skip is not null) return;
        Container = new MsSqlBuilder().WithImage(Images.SqlServer).Build();
        await Container.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public AlertsDb NewDb() => new(new DbContextOptionsBuilder<AlertsDb>()
        .UseSqlServer(Container!.GetConnectionString().Replace("Database=master", "Database=rhw_alerts_test")).Options);

    public async Task DisposeAsync()
    {
        if (Container is not null) await Container.DisposeAsync();
    }
}

/// <summary>Runs the real migrations (including the Quartz job store) and the dedup rules against SQL Server 2022.</summary>
public class AlertsSqlServerTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2025, 6, 10, 9, 0, 0, TimeSpan.Zero));

    private (AlertService Service, AlertsDb Db) NewService()
    {
        var db = sql.NewDb();
        var outbox = new EventOutbox<AlertsDb>(db, _clock, new ServiceIdentity("alerts"));
        var afterCommit = new PostCommitActions();
        var manager = new EscalationManager(db, outbox, NSubstitute.Substitute.For<IEscalationScheduler>(),
            NSubstitute.Substitute.For<IAlertNotifier>(), afterCommit, _clock, NullLogger<EscalationManager>.Instance);
        return (new AlertService(db, outbox, manager, afterCommit, _clock), db);
    }

    private static AlertCandidate Candidate(Guid patient, AlertSeverity severity = AlertSeverity.Critical) =>
        new(patient, AlertType.ThresholdBreach, severity, VitalType.SpO2, "SpO2", 83, "SpO2 below critical limit (83 vs 84)");

    [DockerFact]
    public async Task Quartz_job_store_tables_exist_after_migration()
    {
        await using var db = sql.NewDb();
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sys.tables WHERE name LIKE 'QRTZ_%'").ToListAsync();
        tables.ShouldContain("QRTZ_TRIGGERS");
        tables.ShouldContain("QRTZ_LOCKS");
        tables.Count.ShouldBe(11);
    }

    [DockerFact]
    public async Task Repeated_breach_merges_into_the_open_alert()
    {
        var patient = Guid.NewGuid();
        var (service, db) = NewService();
        await using (db)
        {
            (await service.RaiseAsync(Candidate(patient), default)).Outcome.ShouldBe(CandidateOutcome.Create);
            (await service.RaiseAsync(Candidate(patient), default)).Outcome.ShouldBe(CandidateOutcome.MergeIntoOpen);
        }

        await using var check = sql.NewDb();
        var alert = await check.Alerts.SingleAsync(a => a.PatientId == patient);
        alert.OccurrenceCount.ShouldBe(2);
        alert.Status.ShouldBe(AlertStatus.NotifiedNurse);
        (await check.OutboxMessages.CountAsync(o => o.Key == patient.ToString() && o.Topic == Topics.AlertRaised)).ShouldBe(1);
    }

    [DockerFact]
    public async Task Filtered_unique_index_rejects_a_second_open_alert_for_the_same_problem()
    {
        var patient = Guid.NewGuid();
        var key = Candidate(patient).DedupKey;
        await using var db = sql.NewDb();
        Alert Open() => new()
        {
            Id = Guid.NewGuid(),
            PatientId = patient,
            DedupKey = key,
            Status = AlertStatus.NotifiedNurse,
            Message = "x",
            RaisedAt = _clock.GetUtcNow(),
            LastOccurredAt = _clock.GetUtcNow(),
            Version = Guid.NewGuid()
        };
        db.Alerts.Add(Open());
        await db.SaveChangesAsync();
        db.Alerts.Add(Open());
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [DockerFact]
    public async Task Resolved_problem_stays_quiet_inside_the_suppression_window()
    {
        var patient = Guid.NewGuid();
        var (service, db) = NewService();
        await using (db)
        {
            var created = await service.RaiseAsync(Candidate(patient, AlertSeverity.Medium), default);
            var manager = new EscalationManager(db, new EventOutbox<AlertsDb>(db, _clock, new ServiceIdentity("alerts")),
                NSubstitute.Substitute.For<IEscalationScheduler>(), NSubstitute.Substitute.For<IAlertNotifier>(), new PostCommitActions(), _clock,
                NullLogger<EscalationManager>.Instance);
            (await manager.ResolveAsync(created.Alert!.Id, Guid.NewGuid(), "sensor repositioned", default)).ShouldBeTrue();

            _clock.Advance(TimeSpan.FromMinutes(5));
            (await service.RaiseAsync(Candidate(patient, AlertSeverity.Medium), default)).Outcome.ShouldBe(CandidateOutcome.Suppressed);
            (await service.RaiseAsync(Candidate(patient, AlertSeverity.Critical), default)).Outcome.ShouldBe(CandidateOutcome.Create);
        }
    }
}
