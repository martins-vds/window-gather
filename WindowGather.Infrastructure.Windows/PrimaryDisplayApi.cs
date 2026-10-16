using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowGather;

internal sealed class PrimaryDisplayApi(NativeDesktop desktop) : IPrimaryDisplayApi
{
    internal string ReadPrimaryId()
    {
        IReadOnlyList<Display> displays = desktop.GetDisplays();
        nint monitor = Native.MonitorFromPoint(new(0, 0), 0);
        var info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        if (monitor == 0 || !Native.GetMonitorInfo(monitor, ref info) || (info.Flags & 1) == 0)
            throw Error("Cannot identify the primary monitor");
        var physical = Device();
        if (!PrimaryNative.EnumDisplayDevicesW(info.Device, 0, ref physical, 1))
            throw Error("Cannot identify the primary physical monitor");
        Display[] candidates = displays.Where(display => display.Id == physical.DeviceId &&
            string.Equals(display.DeviceName, info.Device, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1 || !desktop.GetDisplays().Any(display => display == candidates[0]))
            throw new InvalidOperationException("The primary physical display changed while reading it.");
        return candidates[0].Id;
    }

    public PrimarySnapshot Capture()
    {
        nint previousDpi = Native.SetThreadDpiAwarenessContext(-4);
        if (previousDpi == 0) throw Error("Cannot read physical display coordinates");
        try
        {
            string[] paths = ReadPaths();
            IReadOnlyList<Display> displays = desktop.GetDisplays();
            if (displays.Count == 0 || displays.Select(item => item.Id).Distinct().Count() != displays.Count ||
                displays.Select(item => item.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != displays.Count)
                throw new InvalidOperationException("The physical display identities are ambiguous.");
            var outputs = new List<PrimaryOutput>();
            var inactive = new List<string>();
            for (uint index = 0; ; index++)
            {
                var device = Device();
                if (!PrimaryNative.EnumDisplayDevicesW(null, index, ref device, 0)) break;
                if ((device.Flags & 1) == 0)
                {
                    DisplayMode? registry = ReadMode(device.DeviceName, -2, required: false);
                    if (registry is null)
                        throw new InvalidOperationException("Cannot snapshot persisted settings for an inactive output safely.");
                    if (registry.Width != 0 || registry.Height != 0)
                        throw new InvalidOperationException("A disabled output has pending persisted mode settings; changing primary is unsafe.");
                    inactive.Add(device.DeviceName + ":" + device.Flags + ":" + device.DeviceId + ":" +
                        Convert.ToHexString(registry.Bytes));
                    continue;
                }
                if ((device.Flags & 8) != 0)
                    throw new InvalidOperationException("Mirrored display drivers are not supported.");
                Display display = displays.SingleOrDefault(item =>
                    string.Equals(item.DeviceName, device.DeviceName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("The active display topology changed while reading it.");
                var children = new List<Native.DisplayDevice>();
                for (uint childIndex = 0; ; childIndex++)
                {
                    var child = Device();
                    if (!PrimaryNative.EnumDisplayDevicesW(device.DeviceName, childIndex, ref child, 1)) break;
                    if ((child.Flags & 1) != 0) children.Add(child);
                }
                if (children.Count != 1 || children[0].DeviceId != display.Id)
                    throw new InvalidOperationException("Cloned or ambiguous physical monitor identities are not supported.");
                DisplayMode active = ReadMode(device.DeviceName, -1, true)!;
                DisplayMode saved = ReadMode(device.DeviceName, -2, true)!;
                if ((active.Fields & 0x180020) != 0x180020 || (saved.Fields & 0x180020) != 0x180020 ||
                    active.Width == 0 || active.Height == 0 ||
                    display.Bounds.Left != active.X || display.Bounds.Top != active.Y ||
                    display.Bounds.Width != active.Width || display.Bounds.Height != active.Height)
                    throw new InvalidOperationException("The active display mode and physical monitor geometry disagree.");
                var point = new Native.Point(active.X, active.Y);
                nint monitor = Native.MonitorFromPoint(point, 0);
                if (monitor == 0 || PrimaryNative.GetScaleFactorForMonitor(monitor, out int scale) != 0)
                    throw new InvalidOperationException("Cannot preserve the current physical display scaling.");
                outputs.Add(new(display.Id, display.DeviceName, (device.Flags & 4) != 0, scale, active, saved));
            }
            if (outputs.Count != displays.Count || paths.Length != displays.Count ||
                !paths.SequenceEqual(ReadPaths()))
                throw new InvalidOperationException("The display topology changed while reading it.");
            return new(outputs.OrderBy(output => output.Id, StringComparer.Ordinal).ToArray(),
                paths, inactive.Order(StringComparer.Ordinal).ToArray());
        }
        finally { Native.SetThreadDpiAwarenessContext(previousDpi); }
    }

    public int Change(string? device, DisplayMode? mode, uint flags)
    {
        if (mode is null) return PrimaryNative.ChangeDisplaySettingsExW(device, 0, 0, flags, 0);
        byte[] bytes = mode.Bytes;
        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return PrimaryNative.ChangeDisplaySettingsExW(device, buffer, 0, flags, 0);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static Native.DisplayDevice Device() => new() { Size = (uint)Marshal.SizeOf<Native.DisplayDevice>() };

    private static DisplayMode? ReadMode(string device, int index, bool required)
    {
        // Drivers can append dmDriverExtra bytes. Keep the complete returned blob, not a truncated managed struct.
        byte[] bytes = new byte[DisplayMode.Size + ushort.MaxValue];
        BitConverter.GetBytes((ushort)DisplayMode.Size).CopyTo(bytes, 68);
        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            if (!PrimaryNative.EnumDisplaySettingsExW(device, index, buffer, 0))
            {
                if (required) throw Error("Cannot snapshot the current and persisted display modes");
                return null;
            }
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            int length = checked(DisplayMode.Size + BitConverter.ToUInt16(bytes, 70));
            return new(bytes[..length]);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string[] ReadPaths()
    {
        // DISPLAYCONFIG_PATH_INFO is 72 bytes; DISPLAYCONFIG_MODE_INFO is 64 bytes.
        // The path fingerprint includes adapter/target identities, rotation, scaling and refresh.
        const uint activeOnly = 2;
        int result = PrimaryNative.GetDisplayConfigBufferSizes(activeOnly, out uint pathCount, out uint modeCount);
        if (result != 0) throw new Win32Exception(result, "Cannot inspect active display paths");
        byte[] paths = new byte[checked((int)pathCount * 72)];
        byte[] modes = new byte[checked((int)modeCount * 64)];
        result = PrimaryNative.QueryDisplayConfig(activeOnly, ref pathCount, paths, ref modeCount, modes, 0);
        if (result != 0) throw new Win32Exception(result, "The physical display paths could not be captured safely");
        var fingerprints = new List<string>();
        var sources = new HashSet<string>();
        for (int index = 0; index < pathCount; index++)
        {
            byte[] path = paths.AsSpan(index * 72, 72).ToArray();
            // More than one target for one source is a clone, even if monitor enumeration hides it.
            if (!sources.Add(Convert.ToHexString(path.AsSpan(0, 12))))
                throw new InvalidOperationException("Cloned physical display paths are not supported.");
            Array.Clear(path, 12, 4);
            Array.Clear(path, 32, 4);
            fingerprints.Add(Convert.ToHexString(path));
        }
        return fingerprints.Order(StringComparer.Ordinal).ToArray();
    }

    private static Win32Exception Error(string message) => new(Marshal.GetLastWin32Error(), message);
}

internal static class PrimaryNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevicesW(string? device, uint index, ref Native.DisplayDevice info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsExW(string device, int mode, nint settings, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int ChangeDisplaySettingsExW(string? device, nint mode, nint window, uint flags, nint parameters);
    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] byte[] paths,
        ref uint modeCount, [Out] byte[] modes, nint topology);
    [DllImport("shcore.dll")]
    internal static extern int GetScaleFactorForMonitor(nint monitor, out int scale);
}
