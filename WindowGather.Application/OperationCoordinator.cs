using System.Collections.ObjectModel;

namespace WindowGather;

public enum OperationKind { Refresh, GatherSelected, GatherPointer, Restore, Forget }

public sealed record ApplicationState(ReadOnlyCollection<Display> Displays, Display? RecoveryTarget,
    int BorrowedCount, bool HasRecovery, bool IsBusy, long Revision);

public sealed record OperationReply(bool Accepted, string Summary, ReadOnlyCollection<string> Problems);

public sealed class OperationCoordinator
{
    private readonly GatherEngine engine;
    private int entered;
    private long revision;
    private ApplicationState state;

    public OperationCoordinator(GatherEngine engine)
    {
        this.engine = engine;
        state = Snapshot([], false);
    }

    public ApplicationState State => Volatile.Read(ref state);
    public event Action<ApplicationState>? StateChanged;

    public Task<OperationReply> ExecuteAsync(OperationKind kind, Display? target = null) =>
        RunAsync(() => kind switch
        {
            OperationKind.Refresh => new(0, 0, [], "Displays refreshed."),
            OperationKind.GatherSelected => engine.Gather(target ??
                throw new InvalidOperationException("Choose a destination display.")),
            OperationKind.GatherPointer => engine.Gather(engine.GetPointerDisplay()),
            OperationKind.Restore => engine.Restore(),
            OperationKind.Forget => Forget(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });

    public Task<OperationReply> RunAsync(Func<OperationResult> operation)
    {
        if (Interlocked.CompareExchange(ref entered, 1, 0) != 0)
            return Task.FromResult(new OperationReply(false, "Window Gather is busy. Retry after the current operation.",
                Array.AsReadOnly(Array.Empty<string>())));

        try { Publish(State with { IsBusy = true, Revision = Interlocked.Increment(ref revision) }); }
        catch { Interlocked.Exchange(ref entered, 0); throw; }
        return RunOwnedAsync(operation);
    }

    private async Task<OperationReply> RunOwnedAsync(Func<OperationResult> operation)
    {
        OperationReply reply;
        IReadOnlyList<Display> displays = State.Displays;
        try
        {
            reply = await Task.Run(() =>
            {
                OperationResult result = operation();
                displays = engine.GetDisplays();
                return new OperationReply(true, result.Summary, Array.AsReadOnly(result.Problems.ToArray()));
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            reply = new(true, "The operation could not finish. Recovery data has not been discarded.",
                Array.AsReadOnly(new[] { $"{error.GetType().Name}: {error.Message}" }));
        }
        try { Publish(Snapshot(displays, false)); }
        finally { Interlocked.Exchange(ref entered, 0); }
        return reply;
    }

    private OperationResult Forget()
    {
        engine.Forget();
        return new(0, 0, [], "Recovery forgotten. No windows were moved.");
    }

    private ApplicationState Snapshot(IReadOnlyList<Display> displays, bool busy) => new(
        Array.AsReadOnly(displays.ToArray()), engine.Session?.Target, engine.Session?.Windows.Count ?? 0,
        engine.Session is not null, busy, Interlocked.Increment(ref revision));

    private void Publish(ApplicationState next)
    {
        Volatile.Write(ref state, next);
        StateChanged?.Invoke(next);
    }
}
