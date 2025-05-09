namespace RemoteHealthWatch.ServiceDefaults.Messaging;

/// <summary>
/// Side effects (timers, emails) a handler wants to run only once its transaction has committed.
/// The idempotent runner drains it after commit; outside a consumer, callers drain it themselves.
/// </summary>
public sealed class PostCommitActions
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    public void Add(Func<CancellationToken, Task> action) => _actions.Add(action);

    public async Task RunAsync(CancellationToken ct)
    {
        var pending = _actions.ToArray();
        _actions.Clear();
        foreach (var action in pending) await action(ct);
    }
}
