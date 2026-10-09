using System.Collections.ObjectModel;

namespace WindowGather;

public enum OperationKind { Refresh, GatherSelected, GatherPointer, Restore, Forget }

public sealed record ApplicationState(ReadOnlyCollection<Display> Displays, Display? RecoveryTarget,
    int BorrowedCount, bool HasRecovery, bool IsBusy, long Revision)
{
    public string RecoveryWarning { get; init; } = "";
    public bool IsTopologyCheck { get; init; }
}

public sealed record OperationReply(bool Accepted, string Summary, ReadOnlyCollection<string> Problems)
{
    public bool Announce { get; init; } = true;
    public long CompletionOrder { get; init; }
}

public sealed class OperationCoordinator
{
    private readonly GatherEngine engine;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim topologyGate = new(1, 1);
    private long topologyRequested, topologyChecked;
    private long revision;
    private long completionOrder;
    private ApplicationState state;
    private string topologyProblem = "";
    private bool hasDisplaySnapshot;

    public OperationCoordinator(GatherEngine engine)
    {
        this.engine = engine;
        state = Snapshot([], false);
    }

    public ApplicationState State => Volatile.Read(ref state);
    public bool HasWorkPending => gate.CurrentCount == 0 ||
        Volatile.Read(ref topologyRequested) != Volatile.Read(ref topologyChecked);
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
        if (!gate.Wait(0))
            return Task.FromResult(new OperationReply(false, "Window Gather is busy. Retry after the current operation.",
                Array.AsReadOnly(Array.Empty<string>())) { CompletionOrder = Interlocked.Increment(ref completionOrder) });

        try { Publish(State with { IsBusy = true, Revision = Interlocked.Increment(ref revision) }); }
        catch { gate.Release(); throw; }
        return RunOwnedAsync(() => Task.FromResult(operation()));
    }

    public async Task<OperationReply?> NotifyTopologyChangedAsync()
    {
        long requested = Interlocked.Increment(ref topologyRequested);
        long processed = requested;
        await topologyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (requested <= Volatile.Read(ref topologyChecked)) return null;
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                Publish(State with { IsBusy = true, IsTopologyCheck = true, Revision = Interlocked.Increment(ref revision) });
            }
            catch { gate.Release(); throw; }
            return await RunOwnedAsync(async () =>
            {
                IReadOnlyList<Display> previous = engine.GetDisplays().ToArray();
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    long sampled = Volatile.Read(ref topologyRequested);
                    processed = sampled;
                    await Task.Delay(250).ConfigureAwait(false);
                    IReadOnlyList<Display> current = engine.GetDisplays().ToArray();
                    if (SameTopology(previous, current) && sampled == Volatile.Read(ref topologyRequested))
                        return engine.ReturnAfterDestinationLoss(current);
                    previous = current;
                }
                throw new InvalidOperationException("Display layout is still changing. Recovery is saved; refresh or retry after it settles.");
            }, topology: true).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref topologyChecked, Math.Max(processed, Volatile.Read(ref topologyChecked)));
            topologyGate.Release();
        }
    }

    private static bool SameTopology(IReadOnlyList<Display> first, IReadOnlyList<Display> second) =>
        first.Select(d => (d.Id, d.Bounds, d.WorkArea)).OrderBy(d => d.Id, StringComparer.Ordinal)
            .SequenceEqual(second.Select(d => (d.Id, d.Bounds, d.WorkArea)).OrderBy(d => d.Id, StringComparer.Ordinal));

    private async Task<OperationReply> RunOwnedAsync(Func<Task<OperationResult>> operation, bool topology = false)
    {
        OperationReply reply;
        IReadOnlyList<Display> displays = State.Displays;
        try
        {
            reply = await Task.Run(async () =>
            {
                OperationResult result = await operation().ConfigureAwait(false);
                displays = engine.GetDisplays();
                hasDisplaySnapshot = true;
                topologyProblem = "";
                return new OperationReply(true, result.Summary, Array.AsReadOnly(result.Problems.ToArray()))
                    { Announce = result.Announce };
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (topology) topologyProblem = "Automatic return could not finish: " + error.Message + " Click Restore to retry.";
            reply = new(true, "The operation could not finish. Check the details before retrying.",
                Array.AsReadOnly(new[] { $"{error.GetType().Name}: {error.Message}" }));
        }
        reply = reply with { CompletionOrder = Interlocked.Increment(ref completionOrder) };
        try { Publish(Snapshot(displays, false)); }
        finally { gate.Release(); }
        return reply;
    }

    private OperationResult Forget()
    {
        engine.Forget();
        return new(0, 0, [], "Recovery forgotten. No windows were moved.");
    }

    private ApplicationState Snapshot(IReadOnlyList<Display> displays, bool busy) => new(
        Array.AsReadOnly(displays.ToArray()), engine.Session?.Target, engine.Session?.Windows.Count ?? 0,
        engine.Session is not null, busy, Interlocked.Increment(ref revision))
    {
        RecoveryWarning = topologyProblem.Length > 0 ? topologyProblem :
            hasDisplaySnapshot ? engine.RecoveryWarning(displays) : "",
    };

    private void Publish(ApplicationState next)
    {
        Volatile.Write(ref state, next);
        StateChanged?.Invoke(next);
    }
}
