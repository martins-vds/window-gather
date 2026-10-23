using WindowGather;

int passed = 0;
Run("deferred registry visibility can apply only the complete verified layout", () =>
{
    var api = new FakeApi { DeferRegistry = true };
    new PrimaryDisplayTransaction(api).SetPrimary("physical-b");
    Check(api.State.Outputs[1].Primary && api.State.Outputs[0].Active.X == -1920);
    Check(api.State.Outputs.All(output => output.Active.SameState(output.Registry)));
    Check(api.Calls.Count(call => call.Device is null) == 1);
});
Run("successful but ignored staging cannot be reported as a successful primary change", () =>
{
    var api = new FakeApi { IgnoreStage = true };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.State.Outputs[0].Primary && api.State.Outputs[1].Active.X == 1920);
    Check(api.Calls.Count(call => call.Device is null) == 2);
});
Run("failed apply with deferred registry visibility restores the original layout", () =>
{
    var api = new FakeApi { DeferRegistry = true, FailApply = true };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.State.Outputs[0].Primary && api.State.Outputs[1].Active.X == 1920);
    Check(api.State.Outputs.All(output => output.Active.SameState(output.Registry)));
    Check(api.Calls.Count(call => call.Device is null) == 2);
});
Run("failed deferred staging replaces pending requests without applying them", () =>
{
    var api = new FakeApi { DeferRegistry = true, FailStage = 2 };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.All(call => call.Device is not null));
    Check(api.Change(null, null, 0) == 0);
    Check(api.State.Outputs[0].Primary && api.State.Outputs[1].Active.X == 1920);
});
Run("external registry edits still block apply with deferred staging", () =>
{
    var api = new FakeApi { DeferRegistry = true };
    api.OnCapture = current =>
    {
        if (current.Calls.Count != 4) return;
        current.State = current.State with { Outputs = current.State.Outputs.Select(output =>
            output.Id == "physical-a" ? output with { Registry = output.Registry.At(35, 0) } : output).ToArray() };
    };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.All(call => call.Device is not null));
    Check(api.State.Outputs[0].Registry.X == 35);
});
Run("display transaction errors expose the original staging or apply failure and recovery outcome", () =>
{
    foreach (bool staging in new[] { true, false })
    {
        var api = staging ? new FakeApi { FailStage = 2 } : new FakeApi { FailApply = true };
        InvalidOperationException? failure = null;
        try { new PrimaryDisplayTransaction(api).SetPrimary("physical-b"); }
        catch (InvalidOperationException error) { failure = error; }
        Check(failure is not null && failure.InnerException is not null);
        Check(failure!.Message.Contains(failure.InnerException!.Message, StringComparison.Ordinal));
        Check(failure.Message.Contains(staging ? "staging" : "apply", StringComparison.Ordinal));
        Check(failure.Message.Contains("DISP_CHANGE -1", StringComparison.Ordinal));
        Check(failure.Message.Contains("The original display settings were restored.", StringComparison.Ordinal));
    }
});
Run("display transaction errors preserve the original cause when rollback is blocked", () =>
{
    var api = new FakeApi { FailApply = true, HotplugAtApply = true };
    InvalidOperationException? failure = null;
    try { new PrimaryDisplayTransaction(api).SetPrimary("physical-b"); }
    catch (InvalidOperationException error) { failure = error; }
    Check(failure is not null && failure.InnerException is not null);
    Check(failure!.Message.Contains(failure.InnerException!.Message, StringComparison.Ordinal));
    Check(failure.Message.Contains("Automatic rollback was stopped", StringComparison.Ordinal));
    Check(!failure.Message.Contains("The original display settings were restored.", StringComparison.Ordinal));
    Check(api.Calls.Count(call => call.Device is null) == 1);
});
Run("preflight never tests a displaced current primary as an independent layout", () =>
{
    var api = new FakeApi();
    new PrimaryDisplayTransaction(api).SetPrimary("physical-b");
    var tests = api.Calls.Where(call => (call.Flags & PrimaryDisplayTransaction.Test) != 0).ToArray();
    Check(tests.Length == 2);
    var oldPrimary = tests.Single(call => call.Device == "DISPLAY1");
    Check(oldPrimary.Mode!.X == 0 && oldPrimary.Mode.Y == 0);
    Check((oldPrimary.Flags & PrimaryDisplayTransaction.SetPrimaryFlag) == 0);
    var newPrimary = tests.Single(call => call.Device == "DISPLAY2");
    Check(newPrimary.Mode!.X == 0 && newPrimary.Mode.Y == 0);
    Check((newPrimary.Flags & PrimaryDisplayTransaction.SetPrimaryFlag) != 0);
    Check(api.State.Outputs[0].Active.X == -1920 && api.State.Outputs[1].Primary);
});
Run("driver preflight failure is not ignored or retried with writes", () =>
{
    var api = new FakeApi { TestResult = -1 };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count == 1 && api.Calls[0].Flags == (PrimaryDisplayTransaction.Test | PrimaryDisplayTransaction.SetPrimaryFlag));
    Check(api.State.Outputs.All(output => output.Active.SameState(output.Registry)));
});
Run("secondary modes are preflighted in the live layout and translated only during staging", () =>
{
    var api = new FakeApi();
    api.State = api.State with
    {
        Outputs = [.. api.State.Outputs, new PrimaryOutput("physical-c", "DISPLAY3", false, 100,
            FakeApi.Mode(3840, 200), FakeApi.Mode(3840, 200))],
        Paths = ["path-a", "path-b", "path-c"]
    };
    new PrimaryDisplayTransaction(api).SetPrimary("physical-b");
    var test = api.Calls.Single(call => call.Device == "DISPLAY3" && (call.Flags & PrimaryDisplayTransaction.Test) != 0);
    Check(test.Mode!.X == 3840 && test.Mode.Y == 200);
    Check(api.State.Outputs[2].Active.X == 1920 && api.State.Outputs[2].Active.Y == 0);
});
Run("topology changes after preflight block every staging write", () =>
{
    var api = new FakeApi();
    api.OnCapture = current =>
    {
        if (current.Calls.Count == 2) current.State = current.State with { Paths = ["changed"] };
    };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count == 2 && api.Calls.All(call => (call.Flags & PrimaryDisplayTransaction.Test) != 0));
});
Run("rebase preserves modes, scaling and relative offsets", () =>
{
    var api = new FakeApi();
    new PrimaryDisplayTransaction(api).SetPrimary("physical-b");
    Check(api.State.Outputs[0].Active.X == -1920 && api.State.Outputs[0].Active.Y == -200);
    Check(api.State.Outputs[1].Active.X == 0 && api.State.Outputs[1].Primary);
    Check(api.Calls.Where(call => call.Mode is not null).All(call => call.Mode!.Fields == 0x20));
    Check(api.State.Outputs[0].Active.Bytes[220] == 77 && api.State.Outputs[0].Scale == 125);
    Check(api.Calls.Count(call => (call.Flags & PrimaryDisplayTransaction.Test) != 0) == 2);
});
Run("physical identity is not a reused device name", () =>
{
    var api = new FakeApi();
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("DISPLAY2"));
    Check(api.Calls.Count == 0);
});
Run("already primary is a no-op", () =>
{
    var api = new FakeApi();
    new PrimaryDisplayTransaction(api).SetPrimary("physical-a");
    Check(api.Calls.Count == 0);
});
Run("preflight restart never stages", () =>
{
    var api = new FakeApi { FailTest = true };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.All(call => (call.Flags & PrimaryDisplayTransaction.Test) != 0));
});
Run("staging failure restores registry without applying", () =>
{
    var api = new FakeApi { FailStage = 2 };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.All(call => call.Device is not null));
    Check(api.State.Outputs.All(output => output.Registry.SameState(output.Active)));
});
Run("failed apply compensates only the known topology", () =>
{
    var api = new FakeApi { FailApply = true };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.State.Outputs[0].Primary && api.State.Outputs[1].Active.X == 1920);
    Check(api.Calls.Count(call => call.Device is null) == 2);
});
Run("external hotplug blocks stale rollback", () =>
{
    var api = new FakeApi { HotplugAtApply = true };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count(call => call.Device is null) == 1);
    Check(api.State.Paths.Single() == "changed");
});
Run("pending persisted mode is never consumed", () =>
{
    var api = new FakeApi();
    api.State = api.State with { Outputs = api.State.Outputs.Select((output, index) =>
        index == 0 ? output with { Registry = output.Registry.At(30, 0) } : output).ToArray() };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count == 0);
});
Run("invalid dmLogPixels does not masquerade as a pending display setting", () =>
{
    var api = new FakeApi();
    api.State = api.State with { Outputs = api.State.Outputs.Select(output =>
    {
        byte[] active = output.Active.Bytes;
        BitConverter.GetBytes((ushort)144).CopyTo(active, 166);
        return output with { Active = new DisplayMode(active) };
    }).ToArray() };
    new PrimaryDisplayTransaction(api).SetPrimary("physical-b");
    Check(api.State.Outputs[1].Primary && api.Calls.Count(call => call.Device is null) == 1);
});
Run("valid dmLogPixels differences remain pending and block every display write", () =>
{
    var api = new FakeApi();
    api.State = api.State with { Outputs = api.State.Outputs.Select(output =>
    {
        byte[] active = output.Active.Bytes, registry = output.Registry.Bytes;
        BitConverter.GetBytes(output.Active.Fields | 0x20000u).CopyTo(active, 72);
        BitConverter.GetBytes(output.Registry.Fields | 0x20000u).CopyTo(registry, 72);
        BitConverter.GetBytes((ushort)144).CopyTo(active, 166);
        BitConverter.GetBytes((ushort)96).CopyTo(registry, 166);
        return output with { Active = new DisplayMode(active), Registry = new DisplayMode(registry) };
    }).ToArray() };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count == 0);
});
Run("dmLogPixels validity mismatch cannot bypass pending settings checks", () =>
{
    var api = new FakeApi();
    byte[] registry = api.State.Outputs[0].Registry.Bytes;
    BitConverter.GetBytes(api.State.Outputs[0].Registry.Fields | 0x20000u).CopyTo(registry, 72);
    api.State = api.State with { Outputs = api.State.Outputs.Select((output, index) =>
        index == 0 ? output with { Registry = new DisplayMode(registry) } : output).ToArray() };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count == 0);
});
Run("invalid dmLogPixels is ignored only for comparison and survives position requests", () =>
{
    byte[] bytes = FakeApi.Mode(0, 0).Bytes;
    BitConverter.GetBytes((ushort)144).CopyTo(bytes, 166);
    var mode = new DisplayMode(bytes);
    Check(mode.SameState(FakeApi.Mode(0, 0)) && FakeApi.Mode(0, 0).SameState(mode));
    Check(BitConverter.ToUInt16(mode.Bytes, 166) == 144);
    Check(BitConverter.ToUInt16(mode.At(20, 30).PositionOnly().Bytes, 166) == 144);
});
Run("real mode and opaque driver differences still block pending display writes", () =>
{
    foreach (int offset in new[] { 76, 80, 84, 88, 168, 172, 176, 180, 184, 212, 216, 220 })
    {
        var api = new FakeApi();
        byte[] registry = api.State.Outputs[0].Registry.Bytes;
        registry[offset] ^= 1;
        api.State = api.State with { Outputs = api.State.Outputs.Select((output, index) =>
            index == 0 ? output with { Registry = new DisplayMode(registry) } : output).ToArray() };
        Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
        Check(api.Calls.Count == 0);
    }
});
Run("invalid dmLogPixels mismatch does not prevent guarded rollback after failed apply", () =>
{
    var api = new FakeApi { FailApply = true };
    api.State = api.State with { Outputs = api.State.Outputs.Select(output =>
    {
        byte[] active = output.Active.Bytes;
        BitConverter.GetBytes((ushort)144).CopyTo(active, 166);
        return output with { Active = new DisplayMode(active) };
    }).ToArray() };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count(call => call.Device is null) == 2);
    Check(api.State.Outputs[0].Primary && api.State.Outputs[1].Active.X == 1920);
});
Run("registry edits between stages are not overwritten", () =>
{
    var api = new FakeApi();
    bool edited = false;
    api.OnCapture = current =>
    {
        if (edited || current.Calls.Count != 3) return;
        edited = true;
        current.State = current.State with { Outputs = current.State.Outputs.Select(output =>
            output.Id == "physical-a" ? output with { Registry = output.Registry.At(35, 0) } : output).ToArray() };
    };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count == 3 && api.State.Outputs[0].Registry.X == 35);
});
Run("scaling changes after apply prevent stale rollback", () =>
{
    var api = new FakeApi();
    api.OnCapture = current =>
    {
        if (!current.Calls.Any(call => call.Device is null)) return;
        current.State = current.State with { Outputs = current.State.Outputs.Select(output =>
            output.Id == "physical-a" ? output with { Scale = 175 } : output).ToArray() };
    };
    Throws(() => new PrimaryDisplayTransaction(api).SetPrimary("physical-b"));
    Check(api.Calls.Count(call => call.Device is null) == 1);
    Check(api.State.Outputs[0].Scale == 175);
});
Run("DEVMODE retains opaque bytes and rejects truncation", () =>
{
    DisplayMode mode = FakeApi.Mode(100, -300);
    Check(mode.At(0, 0).PositionOnly().Bytes[220] == 77);
    Throws(() => new DisplayMode(new byte[220]));
});
DurableContractTests.RunAll(Run);
StartupTests.RunAll(Run);
int trackerPassed = TrackerTests.Run();
passed += trackerPassed;
Console.WriteLine($"PASS {trackerPassed} native tracker lifecycle checks");
Console.WriteLine($"Passed {passed} native adapter/durable contract/tracker/startup tests; no native display-setting calls, hooks or startup registry writes executed.");

void Run(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Check(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed.");
}
static void Throws(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected an explicit failure.");
}

internal sealed class FakeApi : IPrimaryDisplayApi
{
    internal PrimarySnapshot State = new([
        new("physical-a", "DISPLAY1", true, 125, Mode(0, 0), Mode(0, 0)),
        new("physical-b", "DISPLAY2", false, 150, Mode(1920, 200), Mode(1920, 200))
    ], ["path-a", "path-b"], []);
    internal readonly List<(string? Device, DisplayMode? Mode, uint Flags)> Calls = [];
    internal bool FailTest, FailApply, HotplugAtApply, DeferRegistry, IgnoreStage;
    internal int FailStage, TestResult;
    internal Action<FakeApi>? OnCapture;
    private int stages;
    private readonly Dictionary<string, DisplayMode> deferred = new(StringComparer.OrdinalIgnoreCase);
    public PrimarySnapshot Capture()
    {
        OnCapture?.Invoke(this);
        return State;
    }
    public int Change(string? device, DisplayMode? mode, uint flags)
    {
        Calls.Add((device, mode, flags));
        if ((flags & PrimaryDisplayTransaction.Test) != 0)
        {
            if (State.Outputs.Any(output => output.Device == device && output.Primary) &&
                (mode!.X != 0 || mode.Y != 0)) return -1;
            return FailTest ? 1 : TestResult;
        }
        if (device is null)
        {
            State = State with { Outputs = State.Outputs.Select(output => output with
            {
                Active = deferred.GetValueOrDefault(output.Device, output.Registry),
                Registry = deferred.GetValueOrDefault(output.Device, output.Registry),
                Primary = deferred.GetValueOrDefault(output.Device, output.Registry).X == 0 &&
                    deferred.GetValueOrDefault(output.Device, output.Registry).Y == 0
            }).ToArray() };
            deferred.Clear();
            if (HotplugAtApply) State = State with { Paths = ["changed"] };
            if (FailApply) { FailApply = false; return -1; }
            return 0;
        }
        if (!IgnoreStage)
        {
            if (DeferRegistry)
                deferred[device] = State.Outputs.Single(output => output.Device == device).Registry.At(mode!.X, mode.Y);
            else
                State = State with { Outputs = State.Outputs.Select(output =>
                    output.Device == device ? output with { Registry = output.Registry.At(mode!.X, mode.Y) } : output).ToArray() };
        }
        return ++stages == FailStage ? -1 : 0;
    }
    internal static DisplayMode Mode(int x, int y)
    {
        byte[] bytes = new byte[221];
        BitConverter.GetBytes((ushort)220).CopyTo(bytes, 68);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 70);
        BitConverter.GetBytes(0x180020u).CopyTo(bytes, 72);
        BitConverter.GetBytes(x).CopyTo(bytes, 76);
        BitConverter.GetBytes(y).CopyTo(bytes, 80);
        BitConverter.GetBytes(1920u).CopyTo(bytes, 172);
        BitConverter.GetBytes(1080u).CopyTo(bytes, 176);
        bytes[220] = 77;
        return new(bytes);
    }
}
