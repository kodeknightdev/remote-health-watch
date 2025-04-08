using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.ServiceDefaults.Auth;

public static class Policies
{
    public const string Staff = "staff";
    public const string Clinician = "clinician";
    public const string ManagePatients = "manage-patients";
    public const string Admin = "admin";
    public const string Internal = "internal";
}

public static class AuthExtensions
{
    public static IServiceCollection AddRemoteHealthWatchAuth(this IServiceCollection services, IConfiguration config, string? hubPath = null)
    {
        var options = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes");
        services.Configure<JwtOptions>(config.GetSection("Jwt"));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                if (hubPath is null) return;
                // browsers can't set headers on the websocket upgrade, SignalR sends the token in the query string
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(hubPath))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Staff))
            .AddPolicy(Policies.Clinician, p => p.RequireRole(Roles.Clinicians))
            .AddPolicy(Policies.ManagePatients, p => p.RequireRole(Roles.CareCoordinator, Roles.Admin))
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Internal, p => p.RequireRole(Roles.Service, Roles.Admin));
        return services;
    }
}
