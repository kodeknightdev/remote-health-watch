using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;
using RemoteHealthWatch.TestSupport;
using RemoteHealthWatch.Vitals.Data;
using RemoteHealthWatch.Vitals.Services;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace RemoteHealthWatch.Vitals.Tests;

public sealed class VitalsInfraFixture : IAsyncLifetime
{
    private MsSqlContainer? _sql;
    private RedisContainer? _redis;
    public IConnectionMultiplexer? Redis { get; private set; }

    public async Task InitializeAsync()
    {
        if (new DockerFactAttribute().Skip is not null) return;
        _sql = new MsSqlBuilder().WithImage(Images.SqlServer).Build();
        _redis = new RedisBuilder().WithImage(Images.Redis).Build();
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());
        await using (var db = NewDb()) await db.Database.MigrateAsync();
        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public VitalsDb NewDb() => new(new DbContextOptionsBuilder<VitalsDb>()
        .UseSqlServer(_sql!.GetConnectionString().Replace("Database=master", "Database=rhw_vitals_test")).Options);

    public async Task DisposeAsync()
    {
        Redis?.Dispose();
        if (_sql is not null) await _sql.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }
}

/// <summary>Write side and read models against SQL Server 2022 (partitioned columnstore) and Redis 7.4.</summary>
public class VitalsIntegrationTests(VitalsInfraFixture infra) : IClassFixture<VitalsInfraFixture>
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2025, 4, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly Guid _patient = Guid.NewGuid();
    private readonly Guid _device = Guid.NewGuid();

    private ReadingsBatchReceived Batch(params (long Seq, VitalType Vital, double Value, int SecondsAgo)[] readings) =>
        new(Guid.NewGuid(), _device, _patient, _clock.GetUtcNow(), 80,
            readings.Select(r => new RawReading(r.Seq, r.Vital, r.Value, r.Vital == VitalType.BloodPressure ? 80 : null,
                CanonicalUnits.For(r.Vital), _clock.GetUtcNow().AddSeconds(-r.SecondsAgo))).ToList());

    private async Task<ProcessOutcome> ProcessAsync(ReadingsBatchReceived batch)
    {
        await using var db = infra.NewDb();
        var processor = new ReadingProcessor(db, new RollupWriter(db), new EventOutbox<VitalsDb>(db, _clock, new ServiceIdentity("vitals")), _clock);
        await using var tx = await db.Database.BeginTransactionAsync();
        var outcome = await processor.ProcessAsync(batch, default);
        await tx.CommitAsync();
        return outcome;
    }

    [DockerFact]
    public async Task Same_batch_delivered_twice_is_stored_once()
    {
        var batch = Batch((1, VitalType.HeartRate, 70, 10), (2, VitalType.SpO2, 97, 10));
        (await ProcessAsync(batch)).Stored.ShouldBe(2);
        var replay = await ProcessAsync(batch with { BatchId = Guid.NewGuid() });
        replay.Stored.ShouldBe(0);
        replay.Duplicates.ShouldBe(2);

        await using var db = infra.NewDb();
        (await db.Readings.CountAsync(r => r.DeviceId == _device)).ShouldBe(2);
        (await db.OutboxMessages.CountAsync(o => o.Key == _patient.ToString())).ShouldBe(1);
    }

    [DockerFact]
    public async Task Out_of_order_reading_is_stored_late_and_rollups_are_recomputed()
    {
        await ProcessAsync(Batch((10, VitalType.HeartRate, 80, 5)));
        var late = await ProcessAsync(Batch((9, VitalType.HeartRate, 60, 6)));
        late.Late.ShouldBe(1);
        late.Readings.Single().Late.ShouldBeTrue();

        await using var db = infra.NewDb();
        var minute = await db.MinuteRollups.SingleAsync(r => r.PatientId == _patient && r.Vital == VitalType.HeartRate);
        minute.Count.ShouldBe(2);
        minute.AvgValue.ShouldBe(70);
        minute.MinValue.ShouldBe(60);
        var hour = await db.HourRollups.SingleAsync(r => r.PatientId == _patient && r.Vital == VitalType.HeartRate);
        hour.Count.ShouldBe(2);
        hour.AvgValue.ShouldBe(70);
    }

    [DockerFact]
    public async Task Readings_land_in_the_partitioned_columnstore()
    {
        await ProcessAsync(Batch((1, VitalType.Temperature, 37.1, 0)));
        await using var db = infra.NewDb();
        var partition = await db.Database.SqlQueryRaw<int>(
            "SELECT TOP 1 $PARTITION.pf_vitals_month(MeasuredAt) AS Value FROM vitals.Readings WHERE DeviceId = {0}", _device).SingleAsync();
        // RANGE RIGHT from 2025-01-01: partition 1 holds everything earlier, so April 2025 is partition 5
        partition.ShouldBe(5);
    }

    [DockerFact]
    public async Task Snapshot_keeps_the_newest_value_per_vital()
    {
        var store = new SnapshotStore(infra.Redis!);
        var now = _clock.GetUtcNow();
        await store.ApplyAsync(_patient, _device, [new NormalizedReading(2, VitalType.HeartRate, 88, null, "bpm", now, false)]);
        await store.ApplyAsync(_patient, _device, [new NormalizedReading(1, VitalType.HeartRate, 60, null, "bpm", now.AddMinutes(-1), true)]);
        (await store.GetAsync(_patient)).Single().Value.ShouldBe(88);
    }
}
