using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

/// <summary>Relays committed outbox rows to Kafka. At-least-once; consumers dedupe on the envelope id.</summary>
public sealed class OutboxDispatcher<TDb>(
    IServiceScopeFactory scopes,
    KafkaProducer producer,
    IOptions<KafkaOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher<TDb>> logger) : BackgroundService
    where TDb : DbContext, IMessagingDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idle = TimeSpan.FromMilliseconds(options.Value.OutboxPollMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                sent = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Outbox dispatch failed, will retry: {Error}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            if (sent < options.Value.OutboxBatchSize)
                await Task.Delay(sent == 0 ? idle : TimeSpan.FromMilliseconds(20), stoppingToken);
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = $"[{entity.GetSchema() ?? "dbo"}].[{entity.GetTableName()}]";
        var size = options.Value.OutboxBatchSize;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // READPAST lets a second replica skip rows another one is already relaying
        var batch = await db.OutboxMessages
            .FromSql($"SELECT TOP ({size}) * FROM {table} WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL AND Attempts < 20 ORDER BY OccurredAt")
            .ToListAsync(ct);
        if (batch.Count == 0) return 0;

        var deliveries = batch.Select(async message =>
        {
            var parent = message.TraceParent is not null && ActivityContext.TryParse(message.TraceParent, null, out var p) ? p : default;
            using var activity = MessagingTelemetry.Source.StartActivity($"{message.Topic} publish", ActivityKind.Producer, parent);
            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.destination.name", message.Topic);
            var headers = new Dictionary<string, string> { ["event-id"] = message.Id.ToString(), ["event-type"] = message.Type };
            if (activity is not null) headers["traceparent"] = activity.Id!;
            else if (message.TraceParent is not null) headers["traceparent"] = message.TraceParent;
            try
            {
                await producer.ProduceAsync(message.Topic, message.Key, message.Payload, headers, ct);
                message.ProcessedAt = clock.GetUtcNow();
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                logger.LogWarning("Outbox message {MessageId} to {Topic} failed (attempt {Attempt}): {Error}",
                    message.Id, message.Topic, message.Attempts, ex.Message);
            }
        });
        // same-key messages stay in order: the idempotent producer keeps per-partition ordering for in-flight sends
        await Task.WhenAll(deliveries);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
