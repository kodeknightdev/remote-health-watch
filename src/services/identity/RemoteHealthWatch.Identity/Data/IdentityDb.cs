using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Identity.Domain;

namespace RemoteHealthWatch.Identity.Data;

public class IdentityDb(DbContextOptions<IdentityDb> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("identity");
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.Role).HasMaxLength(40);
        });
        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.ReplacedByHash).HasMaxLength(64);
            e.Property(x => x.RevokedReason).HasMaxLength(60);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
