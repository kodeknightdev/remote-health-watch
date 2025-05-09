using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public sealed class MessagingBuilder(IServiceCollection services, KafkaOptions options)
{
    public IServiceCollection Services => services;

    /// <summary>Transactional outbox plus processed-event dedupe backed by the service's DbContext.</summary>
    public MessagingBuilder WithOutbox<TDb>() where TDb : DbContext, IMessagingDbContext
    {
        services.AddScoped<IEventOutbox, EventOutbox<TDb>>();
        services.AddScoped<IIdempotentRunner, EfIdempotentRunner<TDb>>();
        services.AddScoped<PostCommitActions>();
        if (options.Enabled) services.AddHostedService<OutboxDispatcher<TDb>>();
        return this;
    }

    /// <summary>Registers a handler on the main topics plus a delayed retry lane.</summary>
    public MessagingBuilder Consume<THandler>(string group, params string[] topics) where THandler : class, IEventHandler
    {
        services.AddScoped<THandler>();
        if (!options.Enabled) return this;

        foreach (var retryLane in new[] { false, true })
        {
            var registration = new ConsumerRegistration(typeof(THandler), group, topics, retryLane);
            services.AddSingleton<IHostedService>(sp => ActivatorUtilities.CreateInstance<KafkaConsumerHost>(sp, registration));
        }
        return this;
    }
}

public static class MessagingExtensions
{
    public static MessagingBuilder AddRemoteHealthWatchMessaging(this IServiceCollection services, IConfiguration config, string serviceName)
    {
        var options = config.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
        services.Configure<KafkaOptions>(config.GetSection("Kafka"));
        services.AddSingleton(new ServiceIdentity(serviceName));
        services.AddSingleton<KafkaProducer>();

        if (options.Enabled)
        {
            services.AddHealthChecks().AddKafka(new Confluent.Kafka.ProducerConfig
            {
                BootstrapServers = options.BootstrapServers,
                MessageTimeoutMs = 3000
            }, topic: "healthchecks", name: "kafka", tags: [ServiceDefaultsExtensions.ReadyTag]);
        }
        return new MessagingBuilder(services, options);
    }
}
