using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RemoteHealthWatch.ServiceDefaults.Health;

public static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddSqlServerReadiness(this IHealthChecksBuilder builder, IConfiguration config, string connectionName) =>
        builder.AddSqlServer(config.GetConnectionString(connectionName) ?? "", name: "sqlserver",
            tags: [ServiceDefaultsExtensions.ReadyTag], timeout: TimeSpan.FromSeconds(5));
}
