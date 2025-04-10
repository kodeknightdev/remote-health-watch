using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace RemoteHealthWatch.ServiceDefaults.Caching;

public static class CachingExtensions
{
    /// <summary>Redis multiplexer, Redis as the L2 for HybridCache, and a readiness check.</summary>
    public static IServiceCollection AddRemoteHealthWatchCaching(this IServiceCollection services, IConfiguration config, string instanceName)
    {
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            var options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false;
            var multiplexer = ConnectionMultiplexer.Connect(options);
            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddStackExchangeRedisCache(o =>
            {
                o.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
                o.InstanceName = $"rhw:{instanceName}:";
            });
            services.AddHealthChecks().AddRedis(_ => multiplexer, name: "redis", tags: [ServiceDefaultsExtensions.ReadyTag]);
        }

        services.AddHybridCache(o =>
        {
            o.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10),
                LocalCacheExpiration = TimeSpan.FromMinutes(2)
            };
        });
        return services;
    }
}
