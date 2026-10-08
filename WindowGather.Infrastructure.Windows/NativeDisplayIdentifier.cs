using System.Runtime.InteropServices;

namespace WindowGather;

public sealed class NativeDisplayIdentifier : IDisposable
{
    private readonly ShellNative.WindowProcedure procedure;
    private readonly string className = "WindowGather.Identify." + Guid.NewGuid().ToString("N");
    private readonly Dictionary<nint, Display> overlays = [];
    private readonly nint brush;
    private bool disposed;
    public IReadOnlyList<nint> Handles => overlays.Keys.ToArray();

    public NativeDisplayIdentifier()
    {
        procedure = ProcessMessage;
        brush = IdentifyNative.CreateSolidBrush(0x009E5A00);
        var definition = new ShellNative.WindowClass
        {
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = ShellNative.GetModuleHandle(null), ClassName = className, Background = brush
        };
        if (brush == 0 || ShellNative.RegisterClass(ref definition) == 0)
        {
            if (brush != 0) IdentifyNative.DeleteObject(brush);
            throw WindowsShell.Error("Cannot create display identification visuals");
        }
    }

    public void ShowDisplays(IReadOnlyList<Display> displays)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Dismiss();
        try
        {
            foreach (Display display in displays)
            {
                Box work = display.WorkArea;
                nint handle = ShellNative.CreateWindowEx(0x08000088, className,
                    display.DeviceName.Replace(@"\\.\", ""), 0x80000000,
                    work.Left + work.Width / 2, work.Top + work.Height / 2, 1, 1,
                    0, 0, ShellNative.GetModuleHandle(null), 0);
                if (handle == 0) throw WindowsShell.Error("Cannot show a display identification label");
                overlays.Add(handle, display);
                double scale = IdentifyNative.GetDpiForWindow(handle) / 96.0;
                int width = Math.Min(work.Width, (int)Math.Round(360 * scale));
                int height = Math.Min(work.Height, (int)Math.Round(160 * scale));
                if (!Native.SetWindowPos(handle, -1, work.Left + (work.Width - width) / 2,
                    work.Top + (work.Height - height) / 2, width, height, 0x0050))
                    throw WindowsShell.Error("Cannot position a display identification label");
                if (IdentifyNative.SetTimer(handle, 1, 3000, 0) == 0)
                    throw WindowsShell.Error("Cannot schedule dismissal of display identification labels");
            }
        }
        catch { Dismiss(); throw; }
    }

    public void Dismiss()
    {
        foreach (nint handle in overlays.Keys.ToArray()) ShellNative.DestroyWindow(handle);
        overlays.Clear();
    }

    private nint ProcessMessage(nint handle, uint message, nuint wParam, nint lParam)
    {
        if (message == 0x0021) return 3;
        if (message == 0x0113)
        {
            ShellNative.DestroyWindow(handle);
            overlays.Remove(handle);
            return 0;
        }
        if (message == 0x000F && overlays.TryGetValue(handle, out Display? display))
        {
            nint dc = IdentifyNative.BeginPaint(handle, out var paint);
            try
            {
                IdentifyNative.GetClientRect(handle, out var bounds);
                IdentifyNative.SetBkMode(dc, 1);
                IdentifyNative.SetTextColor(dc, 0x00FFFFFF);
                double scale = IdentifyNative.GetDpiForWindow(handle) / 96.0;
                var caption = bounds;
                caption.Bottom = bounds.Bottom * 2 / 3;
                Draw(dc, display.DeviceName.Replace(@"\\.\", ""), caption, (int)(40 * scale), 700);
                bounds.Top = caption.Bottom;
                Draw(dc, display.Name, bounds, (int)(16 * scale), 400);
            }
            catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            finally { IdentifyNative.EndPaint(handle, ref paint); }
            return 0;
        }
        return ShellNative.DefWindowProc(handle, message, wParam, lParam);
    }

    private static void Draw(nint dc, string text, Native.Rect rectangle, int height, int weight)
    {
        nint font = IdentifyNative.CreateFont(-height, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        if (font == 0) throw WindowsShell.Error("Cannot draw the display identification text");
        nint previous = IdentifyNative.SelectObject(dc, font);
        try { IdentifyNative.DrawText(dc, text, -1, ref rectangle, 0x8825); }
        finally { IdentifyNative.SelectObject(dc, previous); IdentifyNative.DeleteObject(font); }
    }

    public void Dispose()
    {
        if (disposed) return;
        Dismiss();
        ShellNative.UnregisterClass(className, ShellNative.GetModuleHandle(null));
        IdentifyNative.DeleteObject(brush);
        disposed = true;
        GC.KeepAlive(procedure);
    }
}

internal static class IdentifyNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Paint
    {
        public nint Dc;
        public int Erase;
        public Native.Rect Rectangle;
        public int Restore, Incremental;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [DllImport("gdi32.dll")] internal static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint window, nuint id, uint interval, nint callback);
    [DllImport("user32.dll")] internal static extern nint BeginPaint(nint window, out Paint paint);
    [DllImport("user32.dll")] internal static extern bool EndPaint(nint window, ref Paint paint);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Native.Rect rectangle);
    [DllImport("gdi32.dll")] internal static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] internal static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint outputPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int DrawText(nint dc, string text, int length, ref Native.Rect rectangle, uint format);
}
