using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public sealed class KafkaProducer : IDisposable
{
    private readonly Lazy<IProducer<string, string>> _producer;

    public KafkaProducer(IOptions<KafkaOptions> options, ServiceIdentity service, ILogger<KafkaProducer> logger)
    {
        _producer = new Lazy<IProducer<string, string>>(() => new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = service.Name,
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 5,
            CompressionType = CompressionType.Lz4,
            MessageTimeoutMs = 15000
        })
            .SetErrorHandler((_, e) => logger.LogWarning("Kafka producer error: {Reason}", e.Reason))
            .Build());
    }

    /// <summary>Produces with the current trace context in a traceparent header.</summary>
    public Task<DeliveryResult<string, string>> ProduceAsync(string topic, string key, string payload,
        IDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        var message = new Message<string, string> { Key = key, Value = payload, Headers = [] };
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            message.Headers.Add(name, Encoding.UTF8.GetBytes(value));
        if (Activity.Current is { } activity && headers?.ContainsKey("traceparent") != true)
            message.Headers.Add("traceparent", Encoding.UTF8.GetBytes(activity.Id!));
        return _producer.Value.ProduceAsync(topic, message, ct);
    }

    public void Dispose()
    {
        if (!_producer.IsValueCreated) return;
        _producer.Value.Flush(TimeSpan.FromSeconds(5));
        _producer.Value.Dispose();
    }
}

public sealed record ServiceIdentity(string Name);
