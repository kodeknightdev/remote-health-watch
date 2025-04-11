using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Patients.Data;

public class PatientsDb(DbContextOptions<PatientsDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<CareTeamMember> CareTeamMembers => Set<CareTeamMember>();
    public DbSet<PatientThreshold> Thresholds => Set<PatientThreshold>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("patients");
        b.AddMessagingTables();

        var stringList = new ValueComparer<List<string>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())), v => v.ToList());
        var vitalList = new ValueComparer<List<VitalType>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)), v => v.ToList());

        b.Entity<Patient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Mrn).HasMaxLength(20);
            e.HasIndex(x => x.Mrn).IsUnique();
            e.Property(x => x.FirstName).HasMaxLength(80);
            e.Property(x => x.LastName).HasMaxLength(80);
            e.Property(x => x.Sex).HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.RequestedDeviceSerial).HasMaxLength(40);
            e.Property(x => x.LastPairingError).HasMaxLength(200);
            e.Property(x => x.Conditions)
                .HasConversion(v => string.Join(',', v), v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .HasMaxLength(200)
                .Metadata.SetValueComparer(stringList);
            e.OwnsOne(x => x.Plan, plan =>
            {
                plan.Property(p => p.IntervalSeconds).HasColumnName("PlanIntervalSeconds");
                plan.Property(p => p.Vitals).HasColumnName("PlanVitals")
                    .HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<VitalType>>(v, (JsonSerializerOptions?)null) ?? new List<VitalType>())
                    .HasMaxLength(200)
                    .Metadata.SetValueComparer(vitalList);
            });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.CareTeam).WithOne().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Thresholds).WithOne().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CareTeamMember>(e =>
        {
            e.ToTable("CareTeamMembers");
            e.HasKey(x => new { x.PatientId, x.UserId });
            e.Property(x => x.Role).HasMaxLength(40);
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<PatientThreshold>(e =>
        {
            e.ToTable("Thresholds");
            e.HasKey(x => new { x.PatientId, x.Metric });
            e.Property(x => x.Metric).HasMaxLength(30);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(40);
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.Resource).HasMaxLength(80);
            e.Property(x => x.Service).HasMaxLength(40);
            e.HasIndex(x => new { x.PatientId, x.At });
            e.HasIndex(x => new { x.UserId, x.At });
        });
    }
}
