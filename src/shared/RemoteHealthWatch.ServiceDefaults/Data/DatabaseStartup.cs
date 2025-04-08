using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RemoteHealthWatch.ServiceDefaults.Data;

public static class DatabaseStartup
{
    /// <summary>Applies migrations (and an optional seed) on startup in Development, retrying while SQL Server boots.</summary>
    public static async Task MigrateAsync<TDb>(this IServiceProvider services, IHostEnvironment env,
        Func<TDb, IServiceProvider, Task>? seed = null, bool force = false) where TDb : DbContext
    {
        if (!env.IsDevelopment() && !force) return;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TDb>();
                await db.Database.MigrateAsync();
                if (seed is not null) await seed(db, scope.ServiceProvider);
                return;
            }
            catch (Exception ex) when (attempt < 20)
            {
                logger.LogWarning("Database not ready ({Attempt}/20): {Error}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
