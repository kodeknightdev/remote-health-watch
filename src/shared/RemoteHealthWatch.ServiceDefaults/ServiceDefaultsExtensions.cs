using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using Serilog;
using RemoteHealthWatch.ServiceDefaults.Health;
using RemoteHealthWatch.ServiceDefaults.Logging;

namespace RemoteHealthWatch.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    /// <summary>Logging, problem details, health checks and OpenAPI shared by every .NET service.</summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddSerilog((_, cfg) => cfg
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .Enrich.With<PhiRedactionEnricher>()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {service} {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}"));

        builder.AddRemoteHealthWatchTelemetry(serviceName);

        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString()
                ?? ctx.HttpContext.TraceIdentifier;
            ctx.ProblemDetails.Extensions["service"] = serviceName;
        });

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        builder.Services.AddOpenApi();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);
        return builder;
    }

    public static WebApplication MapServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadyTag) || r.Tags.Contains(LiveTag),
            ResponseWriter = HealthResponseWriter.WriteAsync
        }).AllowAnonymous();
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains(LiveTag) })
            .AllowAnonymous();

        if (app.Environment.IsDevelopmentLike() || app.Configuration.GetValue("OpenApi:Expose", false))
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(o => o.WithTitle(app.Environment.ApplicationName)).AllowAnonymous();
        }
        return app;
    }

    public static bool IsDevelopmentLike(this IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment("Test");
}
