using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Vitals.Data;

public class VitalsDb(DbContextOptions<VitalsDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<VitalReading> Readings => Set<VitalReading>();
    public DbSet<ReadingKey> ReadingKeys => Set<ReadingKey>();
    public DbSet<DeviceCursor> DeviceCursors => Set<DeviceCursor>();
    public DbSet<MinuteRollup> MinuteRollups => Set<MinuteRollup>();
    public DbSet<HourRollup> HourRollups => Set<HourRollup>();
    public DbSet<DeviceState> DeviceStates => Set<DeviceState>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("vitals");
        b.AddMessagingTables();

        b.Entity<VitalReading>(e =>
        {
            e.ToTable("Readings");
            // nonclustered and partition-aligned; the clustered index is the columnstore created in the migration
            e.HasKey(x => new { x.DeviceId, x.Seq, x.MeasuredAt }).IsClustered(false);
            e.Property(x => x.Vital).HasConversion<byte>();
            e.Property(x => x.MeasuredAt).HasColumnType("datetime2(3)");
            e.Property(x => x.ReceivedAt).HasColumnType("datetime2(3)");
        });

        b.Entity<ReadingKey>(e =>
        {
            e.ToTable("ReadingKeys");
            e.HasKey(x => new { x.DeviceId, x.Seq });
            e.Property(x => x.StoredAt).HasColumnType("datetime2(0)");
        });

        b.Entity<DeviceCursor>(e =>
        {
            e.ToTable("DeviceCursors");
            e.HasKey(x => x.DeviceId);
        });

        b.Entity<MinuteRollup>(e => ConfigureRollup(e, "Rollup1m"));
        b.Entity<HourRollup>(e => ConfigureRollup(e, "Rollup1h"));

        b.Entity<DeviceState>(e =>
        {
            e.ToTable("DeviceStates");
            e.HasKey(x => x.DeviceId);
            e.Property(x => x.DeviceType).HasMaxLength(30);
            e.HasIndex(x => x.PatientId);
        });
    }

    private static void ConfigureRollup<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e, string table) where T : Rollup
    {
        e.ToTable(table);
        e.HasKey(x => new { x.PatientId, x.Vital, x.BucketStart });
        e.Property(x => x.Vital).HasConversion<byte>();
        e.Property(x => x.BucketStart).HasColumnType("datetime2(0)");
    }
}
