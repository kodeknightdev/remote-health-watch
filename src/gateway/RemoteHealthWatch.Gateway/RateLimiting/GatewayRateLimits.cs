using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RemoteHealthWatch.Gateway.RateLimiting;

public static class GatewayRateLimits
{
    public const string PerDevice = "per-device";
    public const string PerUser = "per-user";

    /// <summary>Partition key from the device key prefix (rhwd_{prefix}_...). The secret part never reaches the limiter.</summary>
    internal static string DevicePartition(HttpContext ctx)
    {
        var raw = ctx.Request.Headers["X-Device-Key"].ToString();
        var parts = raw.Split('_', 3);
        return parts.Length == 3 && parts[0] == "rhwd" && parts[1].Length == 8 ? parts[1] : "no-key";
    }

    internal static string UserPartition(HttpContext ctx) =>
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

    public static IServiceCollection AddGatewayRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var device = config.GetSection("RateLimits:Device");
        var user = config.GetSection("RateLimits:User");
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                return ValueTask.CompletedTask;
            };
            // token bucket: steady rate for normal uploads, room for a device flushing its buffer after a reconnect
            o.AddPolicy(PerDevice, ctx => RateLimitPartition.GetTokenBucketLimiter(DevicePartition(ctx), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = device.GetValue("TokenLimit", 30),
                TokensPerPeriod = device.GetValue("TokensPerPeriod", 6),
                ReplenishmentPeriod = TimeSpan.FromSeconds(device.GetValue("PeriodSeconds", 5)),
                QueueLimit = 0
            }));
            o.AddPolicy(PerUser, ctx => RateLimitPartition.GetFixedWindowLimiter(UserPartition(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = user.GetValue("PermitLimit", 600),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
        });
    }
}
