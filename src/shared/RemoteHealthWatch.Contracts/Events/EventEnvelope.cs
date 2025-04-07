using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteHealthWatch.Contracts.Events;

/// <summary>Wire format for every Kafka message. Payloads carry surrogate ids only, never names or demographics.</summary>
public sealed record EventEnvelope(
    Guid Id,
    string Type,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string Source,
    string? CorrelationId,
    JsonElement Data)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static EventEnvelope Create<T>(T data, string source, string? correlationId = null, DateTimeOffset? at = null, Guid? id = null)
        where T : IIntegrationEvent =>
        new(id ?? Guid.NewGuid(), T.EventType, T.SchemaVersion, at ?? DateTimeOffset.UtcNow, source, correlationId,
            JsonSerializer.SerializeToElement(data, Json));

    public T DataAs<T>() => Data.Deserialize<T>(Json)
        ?? throw new InvalidOperationException($"Event {Id} has no {typeof(T).Name} payload");

    public bool Is<T>() where T : IIntegrationEvent => Type == T.EventType;

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static EventEnvelope Deserialize(string json) => JsonSerializer.Deserialize<EventEnvelope>(json, Json)
        ?? throw new InvalidOperationException("Empty event envelope");
}

public interface IIntegrationEvent
{
    static abstract string EventType { get; }
    static abstract int SchemaVersion { get; }
}
