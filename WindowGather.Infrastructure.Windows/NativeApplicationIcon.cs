using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowGather;

public sealed class NativeApplicationIcon : IDisposable
{
    public nint LargeIcon { get; private set; }
    public nint SmallIcon { get; private set; }

    public NativeApplicationIcon()
    {
        string executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Cannot locate the executable's application icon.");
        uint count = ExtractIconEx(executable, 0, out nint large, out nint small, 1);
        LargeIcon = large;
        SmallIcon = small;
        if (count is 0 or uint.MaxValue || large == 0 || small == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Cannot load Window Gather's embedded application icon.");
        }
    }

    public void Dispose()
    {
        if (LargeIcon != 0 && !DestroyIcon(LargeIcon))
            System.Diagnostics.Trace.TraceError("Cannot release the Window Gather window icon.");
        if (SmallIcon != 0 && !DestroyIcon(SmallIcon))
            System.Diagnostics.Trace.TraceError("Cannot release the Window Gather tray icon.");
        LargeIcon = SmallIcon = 0;
    }

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint ExtractIconEx(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
