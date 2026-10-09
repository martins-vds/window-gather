using System.ComponentModel;
using WindowGather.Presentation;

namespace WindowGather.Core.Tests;

internal static class Fixture
{
    public static readonly Display A = new("physical-a", @"\\.\DISPLAY1", "Desk",
        new(-1920, 0, 0, 1080), new(-1920, 0, 0, 1040));
    public static readonly Display B = new("physical-b", @"\\.\DISPLAY4", "Treadmill",
        new(0, 0, 1920, 1080), new(0, 40, 1920, 1080));
    public static Placement Place(uint state = 1) => new(2, state, new(-1, -1), new(-1, -1), new(-1820, 100, -1320, 500));
    public static SavedWindow Window(long handle, Display? display = null) =>
        new(handle, 123, 1000, "FixtureWindow", Place(), display ?? A);
    public static (Desktop Desktop, Store Store, GatherEngine Engine) Create()
    {
        var log = new List<string>();
        var desktop = new Desktop(log);
        var store = new Store(log);
        return (desktop, store, new GatherEngine(desktop, store));
    }
}

internal sealed class Desktop(List<string> log) : IDesktop
{
    public List<Display> Displays { get; } = [Fixture.A, Fixture.B];
    public Dictionary<long, SavedWindow> Windows { get; } = new()
    {
        [1] = Fixture.Window(1), [2] = Fixture.Window(2, Fixture.B)
    };
    public Dictionary<long, string> Markers { get; } = [];
    public List<long> Moved { get; } = [];
    public List<long> Restored { get; } = [];
    public long FailMove { get; set; }
    public long FailRestore { get; set; }
    public Display Pointer { get; set; } = Fixture.B;
    public int PointerReads { get; private set; }
    public Action? BeforeMove { get; set; }
    public List<string> Warnings { get; } = [];
    public bool FailDisplays { get; set; }
    public Action? BeforeReadDisplays { get; set; }
    public Action<SavedWindow>? BeforeRestore { get; set; }
    public IReadOnlyList<Display> GetDisplays()
    {
        BeforeReadDisplays?.Invoke();
        return FailDisplays ? throw new InvalidOperationException("Display failure") : Displays.ToArray();
    }
    public Display GetPointerDisplay() { PointerReads++; return Pointer; }
    public WindowScan CaptureWindows(string token, Display target)
    {
        log.Add("capture");
        var imported = Windows.Values.Where(w => w.Origin.Id != target.Id).ToList();
        foreach (SavedWindow window in imported) Markers[window.Handle] = token;
        return new(imported, Warnings);
    }
    public bool Matches(SavedWindow window, string token) =>
        Markers.GetValueOrDefault(window.Handle) == token &&
        Windows.TryGetValue(window.Handle, out var current) &&
        current.ProcessId == window.ProcessId && current.ProcessStartedUtcTicks == window.ProcessStartedUtcTicks &&
        current.ClassName == window.ClassName;
    public void Mark(SavedWindow window, string token)
    {
        log.Add("mark");
        if (!Matches(window, token)) throw new InvalidOperationException("Identity mismatch");
    }
    public void Unmark(SavedWindow window, string token) { log.Add("unmark"); Markers.Remove(window.Handle); }
    public void Gather(SavedWindow window, Display target)
    {
        log.Add("move");
        BeforeMove?.Invoke();
        if (window.Handle == FailMove) throw new InvalidOperationException("Move failure");
        Moved.Add(window.Handle);
        Windows[window.Handle] = window with { Origin = target };
    }
    public void Restore(SavedWindow window)
    {
        log.Add("restore");
        BeforeRestore?.Invoke(window);
        if (window.Handle == FailRestore) throw new InvalidOperationException("Restore failure");
        Restored.Add(window.Handle);
        Windows[window.Handle] = window;
    }
}

internal sealed class Store(List<string> log) : ISessionStore
{
    public GatherSession? Current { get; set; }
    public bool FailWrites { get; set; }
    public List<string> Log => log;
    public GatherSession? Load() => Current;
    public void Save(GatherSession session)
    {
        log.Add("save");
        if (FailWrites) throw new IOException("Disk failure");
        Current = session;
    }
    public void Clear()
    {
        log.Add("clear");
        if (FailWrites) throw new IOException("Disk failure");
        Current = null;
    }
}

internal sealed class Hotkeys : IHotkeyBackend
{
    public Dictionary<int, Shortcut> Registered { get; } = [];
    public Shortcut? Blocked { get; set; }
    public List<string> Log { get; } = [];
    public void Register(int id, Shortcut shortcut)
    {
        if (shortcut == Blocked) throw new Win32Exception("Shortcut conflict");
        Registered.Add(id, shortcut);
        Log.Add("register");
    }
    public void Unregister(int id) { Registered.Remove(id); Log.Add("release"); }
}

internal sealed class ShortcutStore(Hotkeys backend) : IShortcutStore
{
    public ShortcutSettings Current { get; private set; } = ShortcutSettings.Defaults;
    public bool FailWrites { get; set; }
    public ShortcutSettings Load() => Current;
    public void Save(ShortcutSettings settings)
    {
        backend.Log.Add("save");
        if (FailWrites) throw new IOException("Shortcut disk failure");
        Current = settings;
    }
}

internal sealed class Dispatcher : IUiDispatcher
{
    public bool Reject { get; set; }
    public bool Queued { get; set; }
    public List<Action> Pending { get; } = [];
    public bool TryEnqueue(Action action)
    {
        if (Reject) return false;
        if (Queued) Pending.Add(action);
        else action();
        return true;
    }
    public void Drain()
    {
        foreach (Action action in Pending.ToArray()) action();
        Pending.Clear();
    }
}
