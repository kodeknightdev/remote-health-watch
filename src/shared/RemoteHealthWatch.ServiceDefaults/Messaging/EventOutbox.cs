using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public interface IEventOutbox
{
    /// <summary>Stages an event in the current DbContext; it is published only if SaveChanges commits.</summary>
    EventEnvelope Enqueue<T>(string topic, Guid key, T @event) where T : IIntegrationEvent;
}

public sealed class EventOutbox<TDb>(TDb db, TimeProvider clock, ServiceIdentity service) : IEventOutbox
    where TDb : DbContext, IMessagingDbContext
{
    public EventEnvelope Enqueue<T>(string topic, Guid key, T @event) where T : IIntegrationEvent
    {
        var envelope = EventEnvelope.Create(@event, service.Name, Activity.Current?.TraceId.ToString(), clock.GetUtcNow());
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = envelope.Id,
            Topic = topic,
            Key = key.ToString(),
            Type = envelope.Type,
            Payload = envelope.Serialize(),
            TraceParent = Activity.Current?.Id,
            OccurredAt = envelope.OccurredAt
        });
        return envelope;
    }
}
