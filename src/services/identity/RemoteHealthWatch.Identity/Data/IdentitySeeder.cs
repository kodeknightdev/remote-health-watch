using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Identity.Domain;

namespace RemoteHealthWatch.Identity.Data;

/// <summary>Development-only demo staff. Password comes from Seed:DemoPassword (SEED_DEMO_PASSWORD).</summary>
public static class IdentitySeeder
{
    private static readonly (string Email, string Name, string Role, bool OnCall)[] Staff =
    [
        ("doctor@remotehealthwatch.local", "Dr Maya Lindqvist", Roles.Doctor, true),
        ("nurse@remotehealthwatch.local", "Sam Okafor", Roles.Nurse, false),
        ("coordinator@remotehealthwatch.local", "Priya Desai", Roles.CareCoordinator, false),
        ("admin@remotehealthwatch.local", "Programme Admin", Roles.Admin, false),
        ("doctor2@remotehealthwatch.local", "Dr Tomas Brandt", Roles.Doctor, false),
        ("nurse2@remotehealthwatch.local", "Hannah Reyes", Roles.Nurse, false),
        ("nurse3@remotehealthwatch.local", "Liam Gallagher", Roles.Nurse, false)
    ];

    public static async Task SeedAsync(IdentityDb db, IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password)) return;

        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.Staff)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });

        var users = services.GetRequiredService<UserManager<AppUser>>();
        foreach (var (email, name, role, onCall) in Staff)
        {
            if (await db.Users.AnyAsync(u => u.Email == email)) continue;
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = name,
                Role = role,
                IsOnCall = onCall,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Seeding {role} failed: {string.Join(", ", result.Errors.Select(e => e.Code))}");
            await users.AddToRoleAsync(user, role);
        }
    }
}
