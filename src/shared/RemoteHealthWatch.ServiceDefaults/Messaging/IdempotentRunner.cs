using Microsoft.EntityFrameworkCore;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

/// <summary>Runs a handler at most once per (event id, consumer).</summary>
public interface IIdempotentRunner
{
    Task<bool> RunOnceAsync(Guid eventId, string consumer, Func<CancellationToken, Task> work, CancellationToken ct);
}

/// <summary>Records the processed event in the same transaction as the handler's own writes.</summary>
public sealed class EfIdempotentRunner<TDb>(TDb db, TimeProvider clock, PostCommitActions afterCommit) : IIdempotentRunner
    where TDb : DbContext, IMessagingDbContext
{
    public async Task<bool> RunOnceAsync(Guid eventId, string consumer, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == eventId && p.Consumer == consumer, ct))
            return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await work(ct);
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, Consumer = consumer, ProcessedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await afterCommit.RunAsync(ct);
        return true;
    }
}
