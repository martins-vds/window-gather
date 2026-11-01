namespace WindowGather;

public sealed class ShortcutMovement(OperationCoordinator coordinator, Func<Display> pointerDisplay,
    Func<OperationKind, Display?, Task<bool>> confirm)
{
    private int pending;
    public bool IsPending => Volatile.Read(ref pending) != 0;

    public async Task<OperationReply> ExecuteAsync(OperationKind kind)
    {
        if (kind is not (OperationKind.GatherPointer or OperationKind.Restore))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0)
            return Rejected("A shortcut confirmation is already open.");
        try
        {
            Display? target = kind == OperationKind.GatherPointer ? pointerDisplay() : null;
            OperationKind operation = target is null ? OperationKind.Restore : OperationKind.GatherSelected;
            if (!await confirm(operation, target))
                return Rejected("Shortcut movement cancelled. No windows were moved.");
            return await coordinator.ExecuteAsync(operation, target);
        }
        finally { Volatile.Write(ref pending, 0); }
    }

    private static OperationReply Rejected(string summary) =>
        new(false, summary, Array.AsReadOnly(Array.Empty<string>()));
}
