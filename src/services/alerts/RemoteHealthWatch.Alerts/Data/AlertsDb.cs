using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Alerts.Data;

public class AlertsDb(DbContextOptions<AlertsDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertTimelineEntry> Timeline => Set<AlertTimelineEntry>();
    public DbSet<AlertNote> Notes => Set<AlertNote>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("alerts");
        b.AddMessagingTables();

        b.Entity<Alert>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Vital).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Metric).HasMaxLength(20);
            e.Property(x => x.Message).HasMaxLength(300);
            e.Property(x => x.DedupKey).HasMaxLength(100);
            e.Property(x => x.AssignedRole).HasMaxLength(30);
            e.Property(x => x.ResolutionReason).HasMaxLength(200);
            e.Property(x => x.Version).IsConcurrencyToken();
            // at most one open alert per problem; a racing second raise fails and merges on retry
            e.HasIndex(x => x.DedupKey).IsUnique().HasFilter("[Status] <> 'Resolved'");
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => new { x.Status, x.NextEscalationAt });
            e.HasIndex(x => x.RaisedAt);
            e.HasMany(x => x.Timeline).WithOne().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AlertTimelineEntry>(e =>
        {
            e.ToTable("AlertTimeline");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Detail).HasMaxLength(300);
        });

        b.Entity<AlertNote>(e =>
        {
            e.ToTable("AlertNotes");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorName).HasMaxLength(120);
            e.Property(x => x.Text).HasMaxLength(2000);
        });
    }
}
