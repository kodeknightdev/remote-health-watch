using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.TestSupport;

/// <summary>Outbox stand-in that keeps staged events in memory for assertions.</summary>
public sealed class RecordingOutbox : IEventOutbox
{
    public List<(string Topic, Guid Key, EventEnvelope Envelope)> Staged { get; } = [];

    public EventEnvelope Enqueue<T>(string topic, Guid key, T @event) where T : IIntegrationEvent
    {
        var envelope = EventEnvelope.Create(@event, "test");
        Staged.Add((topic, key, envelope));
        return envelope;
    }

    public IEnumerable<T> Of<T>() where T : IIntegrationEvent =>
        Staged.Where(s => s.Envelope.Is<T>()).Select(s => s.Envelope.DataAs<T>());
}
