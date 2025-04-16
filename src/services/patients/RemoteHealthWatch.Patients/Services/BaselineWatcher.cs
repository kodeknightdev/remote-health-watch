using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;

namespace RemoteHealthWatch.Patients.Services;

public sealed class EnrollmentOptions
{
    /// <summary>How long a newly paired patient builds a baseline before monitoring is considered active.</summary>
    public double BaselineHours { get; set; } = 24;
}

/// <summary>Moves patients from BaselineBuilding to MonitoringActive once their baseline window has elapsed.</summary>
public sealed class BaselineWatcher(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration config, ILogger<BaselineWatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var window = TimeSpan.FromHours(config.GetSection("Enrollment").Get<EnrollmentOptions>()?.BaselineHours ?? 24);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PatientsDb>();
                var service = scope.ServiceProvider.GetRequiredService<PatientService>();
                var cutoff = clock.GetUtcNow() - window;
                var due = await db.Patients
                    .Where(p => p.Status == MonitoringStatus.BaselineBuilding && p.BaselineStartedAt <= cutoff)
                    .Take(100).ToListAsync(stoppingToken);
                foreach (var patient in due)
                {
                    var transition = EnrollmentStateMachine.OnBaselineElapsed(patient, clock.GetUtcNow(), window);
                    if (transition is null) continue;
                    patient.Version = Guid.NewGuid();
                    service.PublishTransition(patient, transition);
                }
                if (due.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("{Count} patients finished baseline and are now actively monitored", due.Count);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // a device event touched the same patient, next tick picks it up again
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Baseline sweep failed: {Error}", ex.Message);
            }
        }
    }
}
