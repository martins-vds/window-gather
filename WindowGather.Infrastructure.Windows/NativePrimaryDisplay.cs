using System.Runtime.InteropServices;

namespace WindowGather;

/// <summary>Changes the primary physical display without changing the relative desktop layout.</summary>
public sealed class NativePrimaryDisplay : IPrimaryDisplay
{
    private static readonly object Gate = new();
    private readonly PrimaryDisplayTransaction transaction;
    private readonly PrimaryDisplayApi api;

    public NativePrimaryDisplay(NativeDesktop desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        api = new(desktop);
        transaction = new(api);
    }

    public string GetPrimaryDisplayId()
    {
        lock (Gate) return api.ReadPrimaryId();
    }

    public void SetPrimaryDisplay(string displayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        lock (Gate) transaction.SetPrimary(displayId);
    }
}

internal interface IPrimaryDisplayApi
{
    PrimarySnapshot Capture();
    int Change(string? device, DisplayMode? mode, uint flags);
}

internal sealed record PrimaryOutput(string Id, string Device, bool Primary, int Scale,
    DisplayMode Active, DisplayMode Registry);
internal sealed record PrimarySnapshot(IReadOnlyList<PrimaryOutput> Outputs,
    IReadOnlyList<string> Paths, IReadOnlyList<string> Inactive);

internal sealed class PrimaryDisplayTransaction(IPrimaryDisplayApi api)
{
    internal const uint UpdateRegistry = 1, Test = 2, SetPrimaryFlag = 0x10, NoReset = 0x10000000;

    internal string ReadPrimary() => Primary(api.Capture()).Id;

    internal void SetPrimary(string id)
    {
        PrimarySnapshot before = api.Capture();
        PrimaryOutput oldPrimary = Primary(before);
        PrimaryOutput target = before.Outputs.SingleOrDefault(output => output.Id == id)
            ?? throw new InvalidOperationException("The selected physical display is no longer active.");
        if (target.Id == oldPrimary.Id) return;
        // A final global apply also consumes persisted settings. Never consume somebody else's pending layout.
        if (before.Outputs.Any(output => !output.Active.SameState(output.Registry)))
            throw new InvalidOperationException("Display settings have pending registry changes. Apply or cancel them in Windows Settings first.");

        var planned = before.Outputs.ToDictionary(output => output.Device,
            output => output.Active.At(checked(output.Active.X - target.Active.X),
                checked(output.Active.Y - target.Active.Y)), StringComparer.OrdinalIgnoreCase);
        PrimaryOutput[] order = before.Outputs.OrderByDescending(output => output.Id == id).ToArray();
        foreach (PrimaryOutput output in order)
            // CDS_TEST sees the live layout, not the future staged layout. In particular,
            // Windows rejects testing the current primary away from the origin on its own.
            // Test the candidate at the origin and other modes at their current positions.
            Success(api.Change(output.Device,
                (output.Id == id ? planned[output.Device] : output.Active).PositionOnly(),
                Test | (output.Id == id ? SetPrimaryFlag : 0)), "preflight");

        RequireSame(before, api.Capture(), before.Outputs.ToDictionary(output => output.Device,
            output => output.Active, StringComparer.OrdinalIgnoreCase), oldPrimary.Id, registry: true);
        var staged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool applied = false;
        try
        {
            foreach (PrimaryOutput output in order)
            {
                PrimarySnapshot latest = api.Capture();
                RequireSame(before, latest, before.Outputs.ToDictionary(item => item.Device,
                    item => item.Active, StringComparer.OrdinalIgnoreCase), oldPrimary.Id, registry: false);
                RequireRegistry(before, latest, planned, staged);
                // Include failed calls: a driver may have persisted data before reporting failure.
                staged.Add(output.Device);
                Success(api.Change(output.Device, planned[output.Device].PositionOnly(),
                    UpdateRegistry | NoReset | (output.Id == id ? SetPrimaryFlag : 0)), "staging");
                RequireRegistry(before, api.Capture(), planned, staged);
            }
            PrimarySnapshot ready = api.Capture();
            RequireSame(before, ready, before.Outputs.ToDictionary(output => output.Device,
                output => output.Active, StringComparer.OrdinalIgnoreCase), oldPrimary.Id, registry: false);
            // Some drivers expose staged registry positions only after the global apply.
            // Until then accept only the original or requested state; verify the full result afterward.
            RequireRegistry(before, ready, planned, staged);
            applied = true;
            Success(api.Change(null, null, 0), "apply");
            RequireSame(before, api.Capture(), planned, id, registry: true);
        }
        catch (Exception failure)
        {
            string recovery;
            try
            {
                Rollback(before, planned, staged, applied, oldPrimary.Id, id);
                recovery = "The original display settings were restored.";
            }
            catch (Exception rollbackFailure)
            {
                recovery = "Automatic rollback was stopped to avoid overwriting changed display settings. " +
                    "Check Windows Display settings. " + rollbackFailure.Message;
            }
            throw new InvalidOperationException($"Cannot change the primary display. {failure.Message} {recovery}", failure);
        }
    }

    private void Rollback(PrimarySnapshot before, Dictionary<string, DisplayMode> planned,
        HashSet<string> staged, bool applied, string originalPrimary, string requestedPrimary)
    {
        PrimarySnapshot current = api.Capture();
        var original = before.Outputs.ToDictionary(output => output.Device, output => output.Active,
            StringComparer.OrdinalIgnoreCase);
        if (applied)
        {
            // Only compensate a known complete layout. Hotplug, driver changes or external rearrangement win.
            if (!Matches(before, current, original, originalPrimary, false) &&
                !Matches(before, current, planned, requestedPrimary, false))
                throw new InvalidOperationException("The active physical topology changed.");
        }
        else RequireSame(before, current, original, originalPrimary, false);
        RequireRegistry(before, current, planned, staged);

        foreach (PrimaryOutput output in before.Outputs.OrderByDescending(output => output.Id == originalPrimary))
        {
            if (!applied && !staged.Contains(output.Device)) continue;
            // Check again before every registry write; do not restore settings to a reused device name.
            PrimarySnapshot latest = api.Capture();
            if (!Matches(before, latest, original, originalPrimary, false) &&
                !(applied && Matches(before, latest, planned, requestedPrimary, false)))
                throw new InvalidOperationException("The physical topology changed during rollback.");
            RequireRegistry(before, latest, planned, staged);
            Success(api.Change(output.Device, output.Registry.PositionOnly(),
                UpdateRegistry | NoReset | (output.Id == originalPrimary ? SetPrimaryFlag : 0)), "rollback staging");
        }
        if (applied)
        {
            PrimarySnapshot latest = api.Capture();
            if (!Matches(before, latest, original, originalPrimary, false) &&
                !Matches(before, latest, planned, requestedPrimary, false))
                throw new InvalidOperationException("The active physical topology changed before rollback apply.");
            RequireRegistry(before, latest, planned, staged);
            Success(api.Change(null, null, 0), "rollback apply");
        }
        RequireSame(before, api.Capture(), original, originalPrimary, true);
    }

    private static PrimaryOutput Primary(PrimarySnapshot snapshot)
    {
        PrimaryOutput[] candidates = snapshot.Outputs.Where(output => output.Primary).ToArray();
        if (candidates.Length != 1 || candidates[0].Active.X != 0 || candidates[0].Active.Y != 0)
            throw new InvalidOperationException("Cannot identify one unambiguous primary physical display.");
        return candidates[0];
    }

    private static void Success(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException(result == 1
                ? $"Windows requires a restart for display {operation}; no restart will be requested."
                : $"Windows rejected display {operation} (DISP_CHANGE {result}).");
    }

    private static void RequireRegistry(PrimarySnapshot before, PrimarySnapshot now,
        Dictionary<string, DisplayMode> planned, HashSet<string> staged)
    {
        if (!SameDevices(before, now)) throw new InvalidOperationException("The physical display topology changed.");
        foreach (PrimaryOutput output in now.Outputs)
        {
            PrimaryOutput previous = before.Outputs.Single(item => item.Id == output.Id);
            DisplayMode expected = staged.Contains(output.Device) ? planned[output.Device] : previous.Registry;
            // A failed call may have left the old value instead. Both are app-owned known states.
            if (!output.Registry.SameState(expected) &&
                !(staged.Contains(output.Device) && output.Registry.SameState(previous.Registry)))
                throw new InvalidOperationException("Persisted display settings changed externally.");
        }
    }

    private static bool SameDevices(PrimarySnapshot before, PrimarySnapshot now) =>
        before.Paths.SequenceEqual(now.Paths) && before.Inactive.SequenceEqual(now.Inactive) &&
        before.Outputs.Count == now.Outputs.Count &&
        before.Outputs.All(output => now.Outputs.Count(item => item.Id == output.Id &&
            string.Equals(item.Device, output.Device, StringComparison.OrdinalIgnoreCase) &&
            item.Scale == output.Scale) == 1);

    private static bool Matches(PrimarySnapshot before, PrimarySnapshot now,
        Dictionary<string, DisplayMode> modes, string primary, bool registry) =>
        SameDevices(before, now) && now.Outputs.All(output =>
            output.Primary == (output.Id == primary) && output.Active.SameState(modes[output.Device]) &&
            (!registry || output.Registry.SameState(modes[output.Device])));

    private static void RequireSame(PrimarySnapshot before, PrimarySnapshot now,
        Dictionary<string, DisplayMode> modes, string primary, bool registry)
    {
        if (!Matches(before, now, modes, primary, registry))
            throw new InvalidOperationException("Windows changed the primary display, physical topology, mode, or scaling unexpectedly.");
    }
}

internal sealed class DisplayMode
{
    internal const int Size = 220;
    private readonly byte[] bytes;
    internal DisplayMode(byte[] bytes)
    {
        if (bytes.Length < Size || BitConverter.ToUInt16(bytes, 68) != Size ||
            bytes.Length != Size + BitConverter.ToUInt16(bytes, 70))
            throw new InvalidOperationException("Windows returned an unsupported DEVMODEW.");
        this.bytes = (byte[])bytes.Clone();
    }
    internal int X => BitConverter.ToInt32(bytes, 76);
    internal int Y => BitConverter.ToInt32(bytes, 80);
    internal uint Width => BitConverter.ToUInt32(bytes, 172);
    internal uint Height => BitConverter.ToUInt32(bytes, 176);
    internal uint Fields => BitConverter.ToUInt32(bytes, 72);
    internal byte[] Bytes => (byte[])bytes.Clone();
    internal DisplayMode At(int x, int y)
    {
        byte[] copy = Bytes;
        BitConverter.GetBytes(x).CopyTo(copy, 76);
        BitConverter.GetBytes(y).CopyTo(copy, 80);
        return new(copy);
    }
    internal DisplayMode PositionOnly()
    {
        byte[] copy = Bytes;
        BitConverter.GetBytes(0x20u).CopyTo(copy, 72); // DM_POSITION; all other fields and driver bytes remain intact.
        return new(copy);
    }
    internal bool SameState(DisplayMode other)
    {
        const uint logPixels = 0x20000; // DM_LOGPIXELS
        if ((Fields & logPixels) != (other.Fields & logPixels)) return false;
        byte[] left = Bytes, right = other.Bytes;
        // Some drivers return current DPI here but zero for registry modes without declaring it valid.
        // Effective monitor scaling is checked separately; never compare an invalid dmLogPixels value.
        if ((Fields & logPixels) == 0)
        {
            Array.Clear(left, 166, 2);
            Array.Clear(right, 166, 2);
        }
        // dmFields describes validity/requested updates, not the resulting display state.
        Array.Clear(left, 72, 4);
        Array.Clear(right, 72, 4);
        return left.AsSpan().SequenceEqual(right);
    }
}
