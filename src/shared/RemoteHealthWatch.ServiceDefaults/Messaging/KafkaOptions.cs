namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public sealed class KafkaOptions
{
    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Main attempt plus retries before a message is parked on the .dlq topic.</summary>
    public int MaxDeliveryAttempts { get; set; } = 4;
    public double RetryBaseDelaySeconds { get; set; } = 2;
    public int OutboxPollMilliseconds { get; set; } = 250;
    public int OutboxBatchSize { get; set; } = 200;
}
