using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Devices.Data;

public class DevicesDb(DbContextOptions<DevicesDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("devices");
        b.AddMessagingTables();
        b.Entity<Device>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Serial).HasMaxLength(40);
            e.HasIndex(x => x.Serial).IsUnique();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Firmware).HasMaxLength(30);
            e.Property(x => x.ApiKeyPrefix).HasMaxLength(8).IsFixedLength();
            e.HasIndex(x => x.ApiKeyPrefix).IsUnique();
            e.Property(x => x.ApiKeyHash).HasMaxLength(64);
            e.Property(x => x.PreviousKeyHash).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.Status, x.LastSeenAt });
        });
    }
}
