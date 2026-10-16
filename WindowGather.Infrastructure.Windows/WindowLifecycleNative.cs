using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowGather;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void WindowLifecycleCallback(nint hook, uint eventId, nint handle,
    int objectId, int childId, uint eventThread, uint eventTime);

internal interface IWindowLifecycleNative
{
    long TickCount64 { get; }
    uint CurrentThreadId { get; }
    uint CurrentProcessId { get; }
    nint SetHook(uint eventId, WindowLifecycleCallback callback);
    void RemoveHook(nint hook);
    void EnumerateWindows(Func<nint, bool> callback);
    bool IsWindow(nint handle);
    nint GetRoot(nint handle);
    uint GetThreadProcessId(nint handle, out uint processId);
}

internal sealed class WindowLifecycleNative : IWindowLifecycleNative
{
    // A failed unhook must not let the callback be collected while user32 still owns it.
    private static readonly ConcurrentDictionary<nint, WindowLifecycleCallback> Roots = new();
    public long TickCount64 => Environment.TickCount64;
    public uint CurrentThreadId => GetCurrentThreadId();
    public uint CurrentProcessId => (uint)Environment.ProcessId;

    public nint SetHook(uint eventId, WindowLifecycleCallback callback)
    {
        const uint outOfContext = 0;
        nint hook = SetWinEventHook(eventId, eventId, 0, callback, 0, 0, outOfContext);
        if (hook == 0) throw Error("Cannot install a window lifecycle hook.");
        Roots[hook] = callback;
        return hook;
    }

    public void RemoveHook(nint hook)
    {
        if (!UnhookWinEvent(hook)) throw Error("Cannot remove a window lifecycle hook.");
        Roots.TryRemove(hook, out _);
    }

    public void EnumerateWindows(Func<nint, bool> callback)
    {
        if (!Native.EnumWindows((handle, _) => callback(handle), 0))
            throw Error("Cannot establish the window tracking baseline.");
    }

    public bool IsWindow(nint handle) => Native.IsWindow(handle);
    public nint GetRoot(nint handle) => GetAncestor(handle, 2);
    public uint GetThreadProcessId(nint handle, out uint processId) =>
        Native.GetWindowThreadProcessId(handle, out processId);

    private static Win32Exception Error(string message) => new(Marshal.GetLastWin32Error(), message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        WindowLifecycleCallback callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
