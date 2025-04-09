using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Identity.Data;
using RemoteHealthWatch.Identity.Domain;
using RemoteHealthWatch.Identity.Services;
using RemoteHealthWatch.ServiceDefaults;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Data;
using RemoteHealthWatch.ServiceDefaults.Health;
using RemoteHealthWatch.ServiceDefaults.Validation;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("identity");
builder.Services.AddRemoteHealthWatchAuth(builder.Configuration);

builder.Services.AddDbContext<IdentityDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDb")));
builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityDb>();

builder.Services.AddScoped<TokenService>();
builder.Services.AddRemoteHealthWatchControllers<Program>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "IdentityDb");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.MapServiceDefaults();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.Services.MigrateAsync<IdentityDb>(app.Environment, IdentitySeeder.SeedAsync);
app.Run();

public partial class Program;
