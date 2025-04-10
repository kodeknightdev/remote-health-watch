using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using RemoteHealthWatch.Contracts.Grpc;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.ServiceDefaults.Access;

public static class AccessExtensions
{
    /// <summary>gRPC client to the patients directory with a service token and the standard resilience pipeline.</summary>
    public static IServiceCollection AddPatientDirectoryClient(this IServiceCollection services, IConfiguration config)
    {
        services.AddServiceTokens(config);
        services.AddGrpcClient<PatientDirectory.PatientDirectoryClient>(o =>
                o.Address = new Uri(config["Services:PatientsGrpc"] ?? "http://localhost:5112"))
            .AddHttpMessageHandler<ServiceTokenHandler>()
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
                // every gRPC call is a POST, so retry on transport failures rather than by method
                o.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException
                    || args.Outcome.Result?.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable);
            });
        services.AddScoped<IPatientAccess, GrpcPatientAccess>();
        return services;
    }

    public static IServiceCollection AddServiceTokens(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton(sp => new ServiceName(sp.GetService<Messaging.ServiceIdentity>()?.Name ?? "remote-health-watch"));
        services.AddSingleton<ServiceTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();
        services.AddTransient<BearerForwardingHandler>();
        return services;
    }
}
