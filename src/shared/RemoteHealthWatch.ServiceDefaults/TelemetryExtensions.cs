using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.ServiceDefaults;

public static class TelemetryExtensions
{
    /// <summary>
    /// Traces (ASP.NET Core, HttpClient incl. gRPC, Kafka produce/consume with traceparent headers) and metrics
    /// (runtime + RemoteHealthWatch.* meters), exported over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set.
    /// Span names, tags and metric labels carry surrogate ids at most, never PHI.
    /// </summary>
    public static WebApplicationBuilder AddRemoteHealthWatchTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var exportMetrics = builder.Configuration.GetValue("Telemetry:ExportMetrics", false);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: "remote-health-watch")
                .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                {
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/alive");
                    // query strings can carry search terms (patient names); drop them from spans
                    o.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                })
                .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage = r => r.RequestUri?.AbsolutePath.EndsWith("/alive") != true)
                .AddSource(MessagingTelemetry.SourceName)
                .AddSource("Yarp.ReverseProxy");
                if (!string.IsNullOrWhiteSpace(otlp)) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("RemoteHealthWatch.*");
                // Jaeger only takes traces; point this at a collector to keep metrics too
                if (!string.IsNullOrWhiteSpace(otlp) && exportMetrics) m.AddOtlpExporter();
            });
        return builder;
    }
}
