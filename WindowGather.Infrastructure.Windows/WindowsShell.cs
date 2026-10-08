using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowGather;

public enum ShellAction { Open, Restore, Identify, Shortcuts, Forget, Exit }

public sealed class WindowsShell : IHotkeyBackend, IDisposable
{
    private readonly ShellNative.WindowProcedure procedure;
    private readonly Func<Action, Task> dispatch;
    private readonly uint taskbarCreated;
    private readonly string className = "WindowGather.Shell." + Guid.NewGuid().ToString("N");
    private readonly uint ownerThread;
    private nint window;
    private bool added;
    private IReadOnlyList<Display> displays = [];
    private bool canGather;
    private bool canRestore;
    private bool busy;
    private long appliedRevision = -1;
    public event Action<int>? HotkeyPressed;
    public event Action<ShellAction>? ActionRequested;
    public event Action<Display>? GatherRequested;
    public event Action<string>? Problem;
    internal nint MessageWindow => window;

    public WindowsShell(Func<Action, Task> dispatch)
    {
        this.dispatch = dispatch;
        ownerThread = ShellNative.GetCurrentThreadId();
        procedure = ProcessMessage;
        taskbarCreated = ShellNative.RegisterWindowMessage("TaskbarCreated");
        var definition = new ShellNative.WindowClass
        {
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = ShellNative.GetModuleHandle(null), ClassName = className
        };
        if (taskbarCreated == 0 || ShellNative.RegisterClass(ref definition) == 0)
            throw Error("Cannot register the notification-area message receiver");
        window = ShellNative.CreateWindowEx(0x08000080, className, "Window Gather shell", 0,
            0, 0, 0, 0, 0, 0, definition.Instance, 0);
        if (window == 0)
        {
            ShellNative.UnregisterClass(className, definition.Instance);
            throw Error("Cannot create the notification-area message receiver");
        }
        try { AddIcon(); }
        catch { Dispose(); throw; }
    }

    public void Update(ApplicationState state)
    {
        if (state.Revision <= appliedRevision) return;
        appliedRevision = state.Revision;
        displays = state.Displays;
        busy = state.IsBusy;
        canGather = !state.IsBusy && !state.HasRecovery;
        canRestore = !state.IsBusy && state.HasRecovery;
        var data = IconData();
        data.Tip = state.HasRecovery ? $"Window Gather - {state.BorrowedCount} borrowed windows" : "Window Gather - ready";
        if (!ShellNative.ShellNotifyIcon(1, ref data))
            throw Error("Cannot update the notification-area icon");
    }

    public void Register(int id, Shortcut shortcut) => OnOwnerThread(() =>
        new NativeHotkeyBackend(() => window).Register(id, shortcut));
    public void Unregister(int id) => OnOwnerThread(() =>
        new NativeHotkeyBackend(() => window).Unregister(id));

    private void OnOwnerThread(Action action)
    {
        if (window == 0) throw new ObjectDisposedException(nameof(WindowsShell));
        if (ShellNative.GetCurrentThreadId() == ownerThread) action();
        else dispatch(action).GetAwaiter().GetResult();
    }

    private void AddIcon()
    {
        var data = IconData();
        if (!ShellNative.ShellNotifyIcon(0, ref data)) throw Error("Cannot create the notification-area icon");
        added = true;
    }

    private ShellNative.NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<ShellNative.NotifyIconData>(), Window = window, Id = 1,
        Flags = 7, Callback = 0x8001, Icon = ShellNative.LoadIcon(0, 32512), Tip = "Window Gather",
        Info = "", InfoTitle = ""
    };

    private nint ProcessMessage(nint handle, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == 0x0312) HotkeyPressed?.Invoke((int)wParam);
            else if (message == taskbarCreated) AddIcon();
            else if (message == 0x8001)
            {
                if (lParam == 0x0203) ActionRequested?.Invoke(ShellAction.Open);
                else if (lParam is 0x0205 or 0x007B) ShowMenu();
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            Problem?.Invoke(error.Message);
        }
        return ShellNative.DefWindowProc(handle, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = ShellNative.CreatePopupMenu();
        if (menu == 0) throw Error("Cannot create the tray menu");
        try
        {
            AddMenu(menu, 1, "Open Window Gather", true);
            for (int i = 0; i < displays.Count; i++)
                AddMenu(menu, (uint)(1000 + i), "Gather onto " + displays[i], canGather);
            AddMenu(menu, 2, "Restore borrowed windows", canRestore);
            AddMenu(menu, 3, "Identify displays", !busy && displays.Count > 0);
            AddMenu(menu, 4, "Change shortcuts...", !busy);
            AddMenu(menu, 5, "Forget recovery...", canRestore);
            AddMenu(menu, 6, "Exit", !busy);
            if (!ShellNative.GetCursorPos(out var point)) throw Error("Cannot read tray-menu position");
            ShellNative.SetForegroundWindow(window);
            uint selected = ShellNative.TrackPopupMenu(menu, 0x0102, point.X, point.Y, 0, window, 0);
            ShellNative.PostMessage(window, 0, 0, 0);
            if (selected >= 1000 && selected - 1000 < displays.Count)
                GatherRequested?.Invoke(displays[(int)selected - 1000]);
            else if (selected is >= 1 and <= 6) ActionRequested?.Invoke((ShellAction)(selected - 1));
        }
        finally { ShellNative.DestroyMenu(menu); }
    }

    private static void AddMenu(nint menu, uint id, string text, bool enabled)
    {
        if (!ShellNative.AppendMenu(menu, enabled ? 0u : 3u, id, text))
            throw Error("Cannot populate the tray menu");
    }

    public void Dispose()
    {
        if (window == 0) return;
        if (added)
        {
            var data = IconData();
            if (!ShellNative.ShellNotifyIcon(2, ref data))
                System.Diagnostics.Trace.TraceError("Cannot remove the Window Gather tray icon.");
        }
        ShellNative.DestroyWindow(window);
        window = 0;
        ShellNative.UnregisterClass(className, ShellNative.GetModuleHandle(null));
        GC.KeepAlive(procedure);
    }

    internal static Win32Exception Error(string message) => new(Marshal.GetLastWin32Error(), message);
}

internal static class ShellNative
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint handle, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClass(ref WindowClass definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] internal static extern nint DefWindowProc(nint handle, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool ShellNotifyIcon(uint action, ref NotifyIconData data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Native.Point point);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rectangle);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint handle, uint message, nuint wParam, nint lParam);
}
