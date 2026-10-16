using WindowGather;

int passed = 0;
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
    internal bool FailTest, FailApply, HotplugAtApply;
    internal int FailStage;
    internal Action<FakeApi>? OnCapture;
    private int stages;
    public PrimarySnapshot Capture()
    {
        OnCapture?.Invoke(this);
        return State;
    }
    public int Change(string? device, DisplayMode? mode, uint flags)
    {
        Calls.Add((device, mode, flags));
        if ((flags & PrimaryDisplayTransaction.Test) != 0) return FailTest ? 1 : 0;
        if (device is null)
        {
            State = State with { Outputs = State.Outputs.Select(output => output with
            {
                Active = output.Registry,
                Primary = output.Registry.X == 0 && output.Registry.Y == 0
            }).ToArray() };
            if (HotplugAtApply) State = State with { Paths = ["changed"] };
            if (FailApply) { FailApply = false; return -1; }
            return 0;
        }
        State = State with { Outputs = State.Outputs.Select(output =>
            output.Device == device ? output with { Registry = output.Registry.At(mode!.X, mode.Y) } : output).ToArray() };
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
