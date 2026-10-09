using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowGather;

public sealed class NativeDesktop : IDesktop
{
    private const uint DefaultNearest = 2;
    private const int GwlExStyle = -20;
    private const long ToolWindow = 0x80;
    private const long NoActivate = 0x08000000;
    private const uint AsyncPlacement = 4;
    private const uint RestoreToMaximized = 2;

    public IReadOnlyList<Display> GetDisplays()
    {
        using var dpi = new DpiScope();
        var displays = new List<Display>();
        Exception? error = null;
        Native.MonitorCallback callback = (nint monitor, nint dc, ref Native.Rect rectangle, nint data) =>
        {
            try { displays.Add(ReadDisplay(monitor)); return true; }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
            {
                error = failure;
                return false;
            }
        };
        bool enumerated = Native.EnumDisplayMonitors(0, 0, callback, 0);
        if (error is not null) throw new InvalidOperationException("Cannot read all active displays.", error);
        if (!enumerated) throw LastError("Cannot enumerate displays");
        return displays.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Display GetPointerDisplay()
    {
        using var dpi = new DpiScope();
        if (!Native.GetCursorPos(out Native.Point point)) throw LastError("Cannot read pointer position");
        return ReadDisplay(Native.MonitorFromPoint(point, DefaultNearest));
    }

    public WindowScan CaptureWindows(string token, Display target)
    {
        using var dpi = new DpiScope();
        var windows = new List<SavedWindow>();
        var warnings = new List<string>();
        Native.WindowCallback callback = (nint handle, nint data) =>
        {
            try
            {
                if (!Eligible(handle) ||
                    ReadDisplay(Native.MonitorFromWindow(handle, DefaultNearest)).Id == target.Id)
                    return true;
                // Mark before capturing identity/placement, so handle reuse during the scan is detectable.
                if (!Native.SetProp(handle, PropertyName(token), 1))
                    throw LastError("Cannot record this window safely. Elevated apps may need administrator access");
                SavedWindow window = CaptureWindow(handle);
                if (!Matches(window, token))
                    throw new InvalidOperationException("The window was replaced during capture.");
                if (window.Origin.Id == target.Id) Unmark(window, token);
                else windows.Add(window);
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException)
            {
                warnings.Add($"Could not capture window {handle}: {error.Message}");
                if (Native.IsWindow(handle) && Native.GetProp(handle, PropertyName(token)) == 1)
                    Native.RemoveProp(handle, PropertyName(token));
            }
            return true;
        };
        if (!Native.EnumWindows(callback, 0))
        {
            foreach (SavedWindow window in windows) Unmark(window, token);
            throw LastError("Cannot enumerate windows");
        }
        return new(windows, warnings);
    }

    public SavedWindow CaptureWindow(nint handle)
    {
        using var dpi = new DpiScope();
        Native.GetWindowThreadProcessId(handle, out uint pid);
        if (pid == 0) throw new InvalidOperationException("The window has closed.");
        using Process process = Process.GetProcessById(checked((int)pid));
        return new(handle.ToInt64(), (int)pid, process.StartTime.ToUniversalTime().Ticks,
            ReadClass(handle), ReadPlacement(handle), ReadDisplay(Native.MonitorFromWindow(handle, DefaultNearest)));
    }

    public bool Matches(SavedWindow window, string token)
    {
        nint handle = new(window.Handle);
        if (!Native.IsWindow(handle) || Native.GetProp(handle, PropertyName(token)) != 1) return false;
        return MatchesIdentity(window);
    }

    private static bool MatchesIdentity(SavedWindow window)
    {
        nint handle = new(window.Handle);
        if (!Native.IsWindow(handle)) return false;
        Native.GetWindowThreadProcessId(handle, out uint pid);
        if (pid != window.ProcessId) return false;
        try
        {
            using Process process = Process.GetProcessById(window.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == window.ProcessStartedUtcTicks &&
                ReadClass(handle) == window.ClassName;
        }
        catch (ArgumentException) { return false; } // The process exited during the identity check.
    }

    public void Mark(SavedWindow window, string token)
    {
        if (!Matches(window, token))
            throw new InvalidOperationException("The original window has closed or changed identity.");
    }

    public void Unmark(SavedWindow window, string token)
    {
        nint handle = new(window.Handle);
        if (Native.IsWindow(handle) && Native.GetProp(handle, PropertyName(token)) == 1)
            Native.RemoveProp(handle, PropertyName(token));
    }

    public void Gather(SavedWindow window, Display target)
    {
        using var dpi = new DpiScope();
        Box normal = Geometry.FitNormal(window.Placement, window.Origin, target);
        PrepareDisplay(window, target, normal);
        Apply(window, window.Placement with
        {
            Normal = normal, Min = new Position(-1, -1), Max = new Position(-1, -1)
        });
        WaitFor(window, target.Id, window.Placement with { Normal = normal }, exactRectangle: false);
    }

    public void Restore(SavedWindow window)
    {
        using var dpi = new DpiScope();
        if (!MatchesIdentity(window))
            throw new InvalidOperationException("The original window has closed or changed identity.");
        Display? origin = GetDisplays().FirstOrDefault(d => d.Id == window.Origin.Id);
        if (origin is null || origin.Bounds != window.Origin.Bounds || origin.WorkArea != window.Origin.WorkArea)
            throw new InvalidOperationException("The origin display changed during restoration. Recovery was kept; retry after the layout settles.");
        PrepareDisplay(window, window.Origin, window.Placement.Normal);
        Apply(window, window.Placement);
        WaitFor(window, window.Origin.Id, window.Placement, exactRectangle: true);
    }

    private static void PrepareDisplay(SavedWindow window, Display display, Box normal)
    {
        nint handle = new(window.Handle);
        if (Native.IsIconic(handle)) return;
        // Move the visible frame first: maximized windows and DPI-changing apps need this
        // before their saved normal placement can be applied reliably.
        bool maximized = Native.IsZoomed(handle);
        Box work = display.WorkArea;
        int x = maximized ? work.Left : normal.Left + work.Left - display.Bounds.Left;
        int y = maximized ? work.Top : normal.Top + work.Top - display.Bounds.Top;
        int width = maximized ? work.Width : normal.Width;
        int height = maximized ? work.Height : normal.Height;
        if (!Native.SetWindowPos(handle, 0, x, y, width, height, 0x4014))
            throw LastError("Cannot move the window frame to the selected display");
        var watch = Stopwatch.StartNew();
        while (ReadDisplay(Native.MonitorFromWindow(handle, DefaultNearest)).Id != display.Id)
        {
            if (watch.ElapsedMilliseconds >= 1200)
                throw new InvalidOperationException("The window frame did not move. Recovery was kept.");
            Thread.Sleep(25);
        }
    }

    private static void Apply(SavedWindow window, Placement placement)
    {
        var native = new Native.WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<Native.WindowPlacement>(),
            Flags = (placement.Flags & RestoreToMaximized) | AsyncPlacement,
            ShowCommand = Geometry.State(placement.ShowCommand) switch { 2 => 7u, 3 => 3u, _ => 4u },
            Min = new Native.Point(placement.Min.X, placement.Min.Y),
            Max = new Native.Point(placement.Max.X, placement.Max.Y),
            Normal = Native.Rect.From(placement.Normal)
        };
        if (!Native.SetWindowPlacement(new nint(window.Handle), ref native))
            throw LastError("Windows rejected the move. Try running as administrator for elevated apps");
    }

    private static void WaitFor(SavedWindow window, string displayId, Placement placement, bool exactRectangle)
    {
        var watch = Stopwatch.StartNew();
        Placement? lastPlacement = null;
        Display? lastDisplay = null;
        int reapplied = 0;
        do
        {
            nint handle = new(window.Handle);
            if (!Native.IsWindow(handle)) throw new InvalidOperationException("The window closed while moving.");
            Placement actual = ReadPlacement(handle);
            Display display = ReadDisplay(Native.MonitorFromWindow(handle, DefaultNearest));
            lastPlacement = actual;
            lastDisplay = display;
            if (display.Id == displayId &&
                Geometry.State(actual.ShowCommand) == Geometry.State(placement.ShowCommand) &&
                (!exactRectangle || actual.Normal == placement.Normal)) return;
            // WM_DPICHANGED can revise geometry after the first asynchronous placement.
            if (exactRectangle && display.Id == displayId && reapplied < 2 &&
                watch.ElapsedMilliseconds >= 250 * (reapplied + 1))
            {
                Apply(window, placement);
                reapplied++;
            }
            Thread.Sleep(25);
        } while (watch.ElapsedMilliseconds < 1200);
        throw new InvalidOperationException(
            "The application did not accept the requested placement. Its recovery entry was kept. " +
            $"Current display: {lastDisplay?.DeviceName}; state: {lastPlacement?.ShowCommand}; " +
            $"normal rectangle: {lastPlacement?.Normal}. Expected state: {placement.ShowCommand}; " +
            $"rectangle: {placement.Normal}.");
    }

    private static bool Eligible(nint handle)
    {
        if (!Native.IsWindowVisible(handle)) return false;
        Native.GetWindowThreadProcessId(handle, out uint pid);
        if (pid == Environment.ProcessId) return false;
        long style = Native.GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        if ((style & (ToolWindow | NoActivate)) != 0) return false;
        if (Native.DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) != 0)
            throw new InvalidOperationException("Cannot determine whether a window belongs to the current desktop.");
        if (cloaked != 0) return false;
        return ReadClass(handle) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW");
    }

    private static Placement ReadPlacement(nint handle)
    {
        var value = new Native.WindowPlacement { Length = (uint)Marshal.SizeOf<Native.WindowPlacement>() };
        if (!Native.GetWindowPlacement(handle, ref value)) throw LastError("Cannot read window placement");
        return new(value.Flags, value.ShowCommand, new(value.Min.X, value.Min.Y),
            new(value.Max.X, value.Max.Y), value.Normal.Box);
    }

    private static string ReadClass(nint handle)
    {
        var buffer = new StringBuilder(256);
        if (Native.GetClassName(handle, buffer, buffer.Capacity) == 0) throw LastError("Cannot read window class");
        return buffer.ToString();
    }

    private static Display ReadDisplay(nint handle)
    {
        var info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(handle, ref info)) throw LastError("Cannot read display information");
        var device = new Native.DisplayDevice { Size = (uint)Marshal.SizeOf<Native.DisplayDevice>() };
        if (!Native.EnumDisplayDevices(info.Device, 0, ref device, 1) || string.IsNullOrWhiteSpace(device.DeviceId))
            throw new InvalidOperationException($"Cannot identify the physical display {info.Device} safely.");
        return new(device.DeviceId, info.Device, device.DeviceString, info.Monitor.Box, info.Work.Box);
    }

    private static string PropertyName(string token) => $"WindowGather.{token}";
    private static Win32Exception LastError(string message) => new(Marshal.GetLastWin32Error(), message);

    private sealed class DpiScope : IDisposable
    {
        private readonly nint previous;
        public DpiScope()
        {
            previous = Native.SetThreadDpiAwarenessContext(-4);
            if (previous == 0) throw LastError("Cannot enable DPI-correct window coordinates");
        }
        public void Dispose()
        {
            if (previous != 0) Native.SetThreadDpiAwarenessContext(previous);
        }
    }
}

internal static class Native
{
    internal delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rectangle, nint data);
    internal delegate bool WindowCallback(nint handle, nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Box Box => new(Left, Top, Right, Bottom);
        public static Rect From(Box b) => new() { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public uint Length, Flags, ShowCommand;
        public Point Min, Max;
        public Rect Normal;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(nint handle, ref WindowPlacement placement);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(nint handle, ref WindowPlacement placement);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern int GetClassName(nint handle, StringBuilder buffer, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetProp(nint handle, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetProp(nint handle, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint RemoveProp(nint handle, string name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(nint handle, int id);
}
