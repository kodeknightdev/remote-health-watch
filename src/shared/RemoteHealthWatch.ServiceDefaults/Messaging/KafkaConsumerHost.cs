using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public interface IEventHandler
{
    Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct);
}

public sealed record ConsumerRegistration(Type HandlerType, string Group, string[] Topics, bool RetryLane);

/// <summary>
/// Consumes the main topics (or their .retry lane), runs the handler once per event id,
/// and routes failures to .retry with backoff and finally to .dlq.
/// </summary>
public sealed class KafkaConsumerHost(
    ConsumerRegistration registration,
    IServiceScopeFactory scopes,
    KafkaProducer producer,
    IOptions<KafkaOptions> options,
    TimeProvider clock,
    ILogger<KafkaConsumerHost> logger) : BackgroundService
{
    private readonly string _consumerName = registration.HandlerType.Name;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        var topics = registration.RetryLane
            ? registration.Topics.Select(t => t + Topics.RetrySuffix).ToArray()
            : registration.Topics;
        var group = registration.RetryLane ? registration.Group + "-retry" : registration.Group;

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = options.Value.BootstrapServers,
                GroupId = group,
                ClientId = $"{group}-{Environment.MachineName}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true,
                // offsets are stored only after the handler finished, so a crash replays rather than skips
                EnableAutoOffsetStore = false,
                AllowAutoCreateTopics = false,
                PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
            })
            .SetErrorHandler((_, e) => logger.LogWarning("Kafka consumer {Group} error: {Reason}", group, e.Reason))
            .SetPartitionsAssignedHandler((_, parts) =>
                logger.LogInformation("Consumer {Group} assigned {Partitions}", group, string.Join(",", parts.Select(p => $"{p.Topic}[{p.Partition.Value}]"))))
            .Build();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                consumer.Subscribe(topics);
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Subscribe to {Topics} failed: {Error}", string.Join(",", topics), ex.Message);
                await Task.Delay(5000, ct);
            }
        }

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            }
            catch (ConsumeException ex)
            {
                // topic-init may not have run yet on a cold start
                logger.LogDebug("Consume error on {Group}: {Reason}", group, ex.Error.Reason);
                await Task.Delay(2000, ct);
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (result is null) continue;

            await HandleWithRetryRoutingAsync(result, ct);
            consumer.StoreOffset(result);
        }
        consumer.Close();
    }

    private async Task HandleWithRetryRoutingAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var headers = result.Message.Headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
                      ?? new Dictionary<string, string>();
        var baseTopic = headers.GetValueOrDefault("x-original-topic") ?? result.Topic;
        var attempt = int.TryParse(headers.GetValueOrDefault("x-attempt"), out var a) ? a : 0;

        if (registration.RetryLane && DateTimeOffset.TryParse(headers.GetValueOrDefault("x-not-before"), out var notBefore))
        {
            var wait = notBefore - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }

        using var activity = MessagingTelemetry.Source.StartActivity($"{baseTopic} process", ActivityKind.Consumer,
            MessagingTelemetry.ExtractParent(result.Message.Headers));
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", baseTopic);
        activity?.SetTag("messaging.consumer.group.name", registration.Group);
        activity?.SetTag("messaging.destination.partition.id", result.Partition.Value);

        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.Deserialize(result.Message.Value);
        }
        catch (Exception ex)
        {
            logger.LogError("Unreadable message on {Topic} offset {Offset}: {Error}", result.Topic, result.Offset.Value, ex.Message);
            await ForwardAsync(baseTopic + Topics.DeadLetterSuffix, result, headers, attempt, ex.Message, ct);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = (IEventHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);
            var runner = scope.ServiceProvider.GetService<IIdempotentRunner>();
            if (runner is null)
                await handler.HandleAsync(envelope, baseTopic, ct);
            else if (!await runner.RunOnceAsync(envelope.Id, _consumerName, c => handler.HandleAsync(envelope, baseTopic, c), ct))
                logger.LogDebug("Skipping duplicate {EventId} for {Consumer}", envelope.Id, _consumerName);
            MessagingMetrics.Consumed(baseTopic, registration.Group, Stopwatch.GetElapsedTime(started), envelope.OccurredAt, clock);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            MessagingMetrics.Failed(baseTopic, registration.Group);
            var next = attempt + 1;
            var target = next >= options.Value.MaxDeliveryAttempts ? baseTopic + Topics.DeadLetterSuffix : baseTopic + Topics.RetrySuffix;
            logger.LogWarning(ex, "Handler {Handler} failed for {EventType} {EventId} (attempt {Attempt}), routing to {Target}",
                _consumerName, envelope.Type, envelope.Id, next, target);
            await ForwardAsync(target, result, headers, next, ex.Message, ct);
        }
    }

    private Task ForwardAsync(string topic, ConsumeResult<string, string> result, Dictionary<string, string> headers,
        int attempt, string error, CancellationToken ct)
    {
        var baseTopic = headers.GetValueOrDefault("x-original-topic") ?? result.Topic;
        headers["x-original-topic"] = baseTopic;
        headers["x-attempt"] = attempt.ToString();
        headers["x-error"] = error.Length > 500 ? error[..500] : error;
        var backoff = TimeSpan.FromSeconds(options.Value.RetryBaseDelaySeconds * Math.Pow(2, Math.Max(0, attempt - 1)));
        headers["x-not-before"] = clock.GetUtcNow().Add(backoff).ToString("O");
        return producer.ProduceAsync(topic, result.Message.Key, result.Message.Value, headers, ct);
    }
}
