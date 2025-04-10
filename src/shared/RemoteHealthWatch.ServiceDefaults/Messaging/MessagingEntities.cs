using Microsoft.EntityFrameworkCore;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = "";
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public string? TraceParent { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Implemented by DbContexts that publish through the outbox or consume idempotently.</summary>
public interface IMessagingDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<ProcessedEvent> ProcessedEvents { get; }
}

public static class MessagingModelExtensions
{
    public static ModelBuilder AddMessagingTables(this ModelBuilder b)
    {
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("Outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Topic).HasMaxLength(100);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.TraceParent).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
        });
        b.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("ProcessedEvents");
            e.HasKey(x => new { x.EventId, x.Consumer });
            e.Property(x => x.Consumer).HasMaxLength(100);
        });
        return b;
    }
}
